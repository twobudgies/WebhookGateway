using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Publishing;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Ingest.Api;

/// <summary>
/// <c>POST /v1/events</c>: authenticate, rate-limit, validate, enqueue, <c>202</c>.
/// Order matters: the cheap checks (key, rate) run before the body is read (docs/architecture.md section 5).
/// </summary>
public static partial class EventsEndpoint
{
    public const int MaxEventTypeLength = 128;
    public const int MaxIdempotencyKeyLength = 128;

    public static void Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/v1/events", HandleAsync)
            .WithName("SendEvent")
            .WithTags("Events")
            .WithSummary("Send an event")
            .WithDescription(
                "Stores the event and answers 202 right away; delivery to the subscribed endpoints happens afterwards, signed and retried. "
                + "202 means accepted and durably stored, not delivered. Send an Idempotency-Key and reuse it when you retry: "
                + "a repeated key returns the original eventId and creates no second event. Retry 429 and 503 after the Retry-After seconds.")
            .Accepts<SendEventRequest>("application/json")
            .Produces<EventAccepted>(StatusCodes.Status202Accepted, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status429TooManyRequests, "application/json")
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json")
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = "Up to 128 characters. Use one key per logical event and reuse it on retries; repeating it within the plan's window (1 hour on basic, 24 hours otherwise) returns the original eventId.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = MaxIdempotencyKeyLength },
                });
                return Task.CompletedTask;
            });
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IApiKeyStore keys,
        IRateLimiter limiter,
        IEventPublisher publisher,
        IIdempotencyStore idempotency,
        IQuotaCounter quota,
        IOptions<QuotaOptions> quotaOptions,
        TimeProvider clock,
        ILogger<IngestApiMarker> logger,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadBearerToken(http.Request);
        if (apiKey is null)
        {
            LogAuthRejected(logger, "missing_or_malformed_header");
            return ApiErrors.Create(StatusCodes.Status401Unauthorized, "unauthorized", "Missing or malformed Authorization header.");
        }

        var tenant = await keys.FindByKeyHashAsync(ApiKeyHasher.Hash(apiKey), cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            LogAuthRejected(logger, "unknown_key");
            return ApiErrors.Create(StatusCodes.Status401Unauthorized, "unauthorized", "Invalid API key.");
        }

        if (tenant.Status != TenantStatus.Active)
        {
            LogTenantNotActive(logger, tenant.TenantId.Value, tenant.Status);
            return ApiErrors.Create(StatusCodes.Status403Forbidden, "tenant_suspended", "This account is not active.");
        }

        var rate = await limiter.TryAcquireAsync(tenant.TenantId, tenant.Limits, cancellationToken).ConfigureAwait(false);
        SetRateLimitHeaders(http.Response, rate);
        if (!rate.Allowed)
        {
            LogRateLimited(logger, tenant.TenantId.Value, rate.Limit);
            return ApiErrors.Create(
                StatusCodes.Status429TooManyRequests,
                "rate_limited",
                $"Plan limit of {rate.Limit} events/second exceeded.");
        }

        var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            LogRejected(logger, tenant.TenantId.Value, "idempotency_key_too_long");
            return ApiErrors.Create(StatusCodes.Status400BadRequest, "invalid_request", $"Idempotency-Key is longer than {MaxIdempotencyKeyLength} characters.");
        }

        var body = await ReadBodyAsync(http.Request, tenant.Limits.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            LogRejected(logger, tenant.TenantId.Value, "payload_too_large");
            return ApiErrors.Create(StatusCodes.Status413PayloadTooLarge, "payload_too_large", $"Request body exceeds {tenant.Limits.MaxPayloadBytes} bytes.");
        }

        var parsed = Parse(body);
        if (parsed.Error is not null)
        {
            LogRejected(logger, tenant.TenantId.Value, "invalid_body");
            return ApiErrors.Create(StatusCodes.Status400BadRequest, "invalid_request", parsed.Error);
        }

        var now = clock.GetUtcNow();
        var message = new EventMessage(
            EventMessage.CurrentSchemaVersion,
            EventId.New(),
            tenant.TenantId,
            parsed.Type!,
            parsed.OccurredAt ?? now,
            now,
            idempotencyKey.Length == 0 ? null : idempotencyKey,
            parsed.Data);

        var count = await quota.IncrementAsync(tenant.TenantId, now, cancellationToken).ConfigureAwait(false);
        if (quotaOptions.Value.Enforce && count > tenant.Limits.MonthlyQuota)
        {
            await quota.DecrementAsync(tenant.TenantId, now, cancellationToken).ConfigureAwait(false);
            LogQuotaExceeded(logger, tenant.TenantId.Value, tenant.Limits.MonthlyQuota);
            return ApiErrors.Create(StatusCodes.Status429TooManyRequests, "quota_exceeded", "Monthly event quota exceeded.");
        }

        if (message.IdempotencyKey is not null)
        {
            var claim = await idempotency.TryClaimAsync(tenant.TenantId, message.IdempotencyKey, message.EventId, tenant.Limits.IdempotencyWindow, cancellationToken).ConfigureAwait(false);
            if (!claim.Claimed)
            {
                await quota.DecrementAsync(tenant.TenantId, now, cancellationToken).ConfigureAwait(false);
                LogDuplicate(logger, tenant.TenantId.Value, claim.ExistingEventId?.Value);
                return Results.Json(new { eventId = claim.ExistingEventId?.Value }, statusCode: StatusCodes.Status202Accepted);
            }
        }

        try
        {
            await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (message.IdempotencyKey is not null)
            {
                await idempotency.ReleaseAsync(tenant.TenantId, message.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            }

            await quota.DecrementAsync(tenant.TenantId, now, cancellationToken).ConfigureAwait(false);
            LogPublishFailed(logger, ex, tenant.TenantId.Value, message.EventId.Value);
            return ApiErrors.Create(StatusCodes.Status503ServiceUnavailable, "unavailable", "The event could not be stored. Retry the request.");
        }

        LogAccepted(logger, tenant.TenantId.Value, message.EventId.Value, message.Type);
        return Results.Json(new { eventId = message.EventId.Value }, statusCode: StatusCodes.Status202Accepted);
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[scheme.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private static void SetRateLimitHeaders(HttpResponse response, RateLimitResult rate)
    {
        response.Headers["RateLimit-Limit"] = rate.Limit.ToString(CultureInfo.InvariantCulture);
        response.Headers["RateLimit-Remaining"] = rate.Remaining.ToString(CultureInfo.InvariantCulture);
        if (!rate.Allowed)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(rate.RetryAfter.TotalSeconds));
            response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
            response.Headers["RateLimit-Reset"] = seconds.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <returns>The body bytes, or null when it is larger than <paramref name="maxBytes"/>.</returns>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ParsedEvent Parse(byte[] body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return ParsedEvent.Fail("Body is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParsedEvent.Fail("Body must be a JSON object.");
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                return ParsedEvent.Fail("'type' is required and must be a string.");
            }

            var type = typeElement.GetString()!;
            if (type.Length is 0 or > MaxEventTypeLength || !EventTypePattern().IsMatch(type))
            {
                return ParsedEvent.Fail($"'type' must be 1-{MaxEventTypeLength} characters of letters, digits, '.', '_', ':' or '-'.");
            }

            DateTimeOffset? occurredAt = null;
            if (root.TryGetProperty("occurredAt", out var occurredElement) && occurredElement.ValueKind != JsonValueKind.Null)
            {
                if (occurredElement.ValueKind != JsonValueKind.String || !occurredElement.TryGetDateTimeOffset(out var parsedAt))
                {
                    return ParsedEvent.Fail("'occurredAt' must be an ISO-8601 timestamp.");
                }

                occurredAt = parsedAt;
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return ParsedEvent.Fail("'data' is required and must be a JSON object.");
            }

            return new ParsedEvent(type, occurredAt, data.Clone(), null);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request rejected: authentication failed ({Reason})")]
    private static partial void LogAuthRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected: tenant {TenantId} is {Status}")]
    private static partial void LogTenantNotActive(ILogger logger, string tenantId, TenantStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected: tenant {TenantId} exceeded {Limit} events/second")]
    private static partial void LogRateLimited(ILogger logger, string tenantId, int limit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request rejected for tenant {TenantId}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string tenantId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {EventId} of tenant {TenantId} could not be published; responding 503")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string tenantId, string eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected: tenant {TenantId} exceeded its monthly quota of {Quota} events")]
    private static partial void LogQuotaExceeded(ILogger logger, string tenantId, long quota);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate request for tenant {TenantId}; returning the original event {EventId}")]
    private static partial void LogDuplicate(ILogger logger, string tenantId, string? eventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accepted event {EventId} ({EventType}) of tenant {TenantId}")]
    private static partial void LogAccepted(ILogger logger, string tenantId, string eventId, string eventType);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EventTypePattern();

    private readonly record struct ParsedEvent(string? Type, DateTimeOffset? OccurredAt, JsonElement Data, string? Error)
    {
        public static ParsedEvent Fail(string error) => new(null, null, default, error);
    }
}
