using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        app.MapPost("/v1/events", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IApiKeyStore keys,
        IRateLimiter limiter,
        IEventPublisher publisher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadBearerToken(http.Request);
        if (apiKey is null)
        {
            return ApiErrors.Create(StatusCodes.Status401Unauthorized, "unauthorized", "Missing or malformed Authorization header.");
        }

        var tenant = await keys.FindByKeyHashAsync(ApiKeyHasher.Hash(apiKey), cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return ApiErrors.Create(StatusCodes.Status401Unauthorized, "unauthorized", "Invalid API key.");
        }

        if (tenant.Status != TenantStatus.Active)
        {
            return ApiErrors.Create(StatusCodes.Status403Forbidden, "tenant_suspended", "This account is not active.");
        }

        var rate = await limiter.TryAcquireAsync(tenant.TenantId, tenant.Limits, cancellationToken).ConfigureAwait(false);
        SetRateLimitHeaders(http.Response, rate);
        if (!rate.Allowed)
        {
            return ApiErrors.Create(
                StatusCodes.Status429TooManyRequests,
                "rate_limited",
                $"Plan limit of {rate.Limit} events/second exceeded.");
        }

        var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return ApiErrors.Create(StatusCodes.Status400BadRequest, "invalid_request", $"Idempotency-Key is longer than {MaxIdempotencyKeyLength} characters.");
        }

        var body = await ReadBodyAsync(http.Request, tenant.Limits.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return ApiErrors.Create(StatusCodes.Status413PayloadTooLarge, "payload_too_large", $"Request body exceeds {tenant.Limits.MaxPayloadBytes} bytes.");
        }

        var parsed = Parse(body);
        if (parsed.Error is not null)
        {
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

        await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);

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

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EventTypePattern();

    private readonly record struct ParsedEvent(string? Type, DateTimeOffset? OccurredAt, JsonElement Data, string? Error)
    {
        public static ParsedEvent Fail(string error) => new(null, null, default, error);
    }
}
