using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebhookGateway.ControlPlane.Data;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Signing;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.ControlPlane.Api;

public sealed class AdminOptions
{
    public const string SectionName = "ControlPlane";

    /// <summary>Operator key for <c>/admin/v1</c> (header <c>X-Admin-Key</c>). Empty disables the admin API (it answers 503).</summary>
    public string? AdminKey { get; init; }

    /// <summary>Allow http:// endpoint URLs (development only).</summary>
    public bool AllowInsecureHttp { get; init; }

    /// <summary>Allow endpoint URLs on private networks (private installs only, see SenderOptions.AllowPrivateNetworks).</summary>
    public bool AllowPrivateNetworks { get; init; }
}

/// <summary>
/// Operator API (v1: tenants are created by an operator, no self-service). Writes the configuration tables; the other
/// services read them through their caches (up to 30 s until a change is visible). Secrets and keys are shown once.
/// </summary>
public static partial class AdminApi
{
    public static void Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var admin = app.MapGroup("/admin/v1")
            .AddEndpointFilter(RequireAdminKeyAsync)
            .WithMetadata(
                new ProducesResponseTypeAttribute<ErrorBody>(StatusCodes.Status401Unauthorized, "application/json"),
                new ProducesResponseTypeAttribute<ErrorBody>(StatusCodes.Status503ServiceUnavailable, "application/json"));

        admin.MapPost("/tenants", CreateTenantAsync)
            .WithTags("Tenants").WithName("CreateTenant").WithSummary("Create a customer (tenant)")
            .WithDescription("Plan is basic, advanced or premium. Plans differ in rate limit, monthly quota, endpoint count and retry window.")
            .Produces<TenantView>(StatusCodes.Status201Created).Produces<ErrorBody>(StatusCodes.Status400BadRequest);
        admin.MapGet("/tenants/{tenantId}", GetTenantAsync)
            .WithTags("Tenants").WithName("GetTenant").WithSummary("Read a customer")
            .Produces<TenantView>().Produces<ErrorBody>(StatusCodes.Status404NotFound);
        admin.MapPatch("/tenants/{tenantId}", UpdateTenantAsync)
            .WithTags("Tenants").WithName("UpdateTenant").WithSummary("Change a customer's status or plan")
            .WithDescription("status: active, suspended (Ingest answers 403 and deliveries pause) or closed. A plan change is refused (409) if the customer has more endpoints than the new plan allows.")
            .Produces<TenantView>().Produces<ErrorBody>(StatusCodes.Status400BadRequest).Produces<ErrorBody>(StatusCodes.Status404NotFound).Produces<ErrorBody>(StatusCodes.Status409Conflict);
        admin.MapPost("/tenants/{tenantId}/api-keys", CreateApiKeyAsync)
            .WithTags("API keys").WithName("CreateApiKey").WithSummary("Create an API key for a customer")
            .WithDescription("The full key is in this response only. Only a hash and a 12-character prefix are stored.")
            .Produces<ApiKeyCreated>(StatusCodes.Status201Created).Produces<ErrorBody>(StatusCodes.Status404NotFound);
        admin.MapGet("/tenants/{tenantId}/api-keys", ListApiKeysAsync)
            .WithTags("API keys").WithName("ListApiKeys").WithSummary("List a customer's API keys (prefixes only)")
            .Produces<ApiKeyInfo[]>();
        admin.MapDelete("/tenants/{tenantId}/api-keys/{keyId}", RevokeApiKeyAsync)
            .WithTags("API keys").WithName("RevokeApiKey").WithSummary("Revoke an API key")
            .Produces(StatusCodes.Status204NoContent).Produces<ErrorBody>(StatusCodes.Status404NotFound);
        admin.MapPost("/tenants/{tenantId}/endpoints", CreateEndpointAsync)
            .WithTags("Endpoints").WithName("CreateEndpoint").WithSummary("Register an endpoint and its event types")
            .WithDescription("The URL must be https (http and private networks only if the install allows them) and must pass the SSRF checks. eventTypes may contain * for all events. The signing secret is in this response only.")
            .Produces<EndpointCreated>(StatusCodes.Status201Created).Produces<ErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<ErrorBody>(StatusCodes.Status404NotFound).Produces<ErrorBody>(StatusCodes.Status409Conflict);
        admin.MapGet("/tenants/{tenantId}/endpoints", ListEndpointsAsync)
            .WithTags("Endpoints").WithName("ListEndpoints").WithSummary("List a customer's endpoints with their event types")
            .Produces<EndpointInfo[]>();
        admin.MapPatch("/tenants/{tenantId}/endpoints/{endpointId}", UpdateEndpointAsync)
            .WithTags("Endpoints").WithName("UpdateEndpoint").WithSummary("Change an endpoint's URL or enable/disable it")
            .WithDescription("status: active or disabled. A disabled endpoint receives no new deliveries.")
            .Produces<EndpointStatusView>().Produces<ErrorBody>(StatusCodes.Status400BadRequest).Produces<ErrorBody>(StatusCodes.Status404NotFound);
        admin.MapPut("/tenants/{tenantId}/endpoints/{endpointId}/subscriptions", SetSubscriptionsAsync)
            .WithTags("Endpoints").WithName("SetSubscriptions").WithSummary("Replace the event types an endpoint receives")
            .Produces<SubscriptionsView>().Produces<ErrorBody>(StatusCodes.Status400BadRequest).Produces<ErrorBody>(StatusCodes.Status404NotFound);
    }

    private static async ValueTask<object?> RequireAdminKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        var log = context.HttpContext.RequestServices.GetRequiredService<ILogger<AdminOptionsMarker>>();
        if (string.IsNullOrEmpty(options.AdminKey))
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "admin_disabled", "The admin API is not configured.");
        }

        var provided = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.AdminKey))))
        {
            LogAdminRejected(log, context.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Error(StatusCodes.Status401Unauthorized, "unauthorized", "Missing or invalid admin key.");
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async Task<IResult> CreateTenantAsync(CreateTenant request, ConfigDbContext db, ILogger<AdminOptionsMarker> log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "'name' is required (at most 200 characters).");
        }

        var plan = request.Plan?.ToLowerInvariant();
        if (plan is null || !await db.Plans.AnyAsync(p => p.Code == plan, ct).ConfigureAwait(false))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "'plan' must be an existing plan code (basic, advanced, premium).");
        }

        var tenant = new TenantRow { Id = Guid.CreateVersion7(), Name = request.Name.Trim(), PlanCode = plan, CreatedAt = DateTimeOffset.UtcNow };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogTenantCreated(log, tenant.Id, plan);
        return Results.Json(TenantView(tenant), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetTenantAsync(Guid tenantId, ConfigDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        return tenant is null ? NotFound("tenant") : Results.Json(TenantView(tenant));
    }

    private static async Task<IResult> UpdateTenantAsync(Guid tenantId, UpdateTenant request, ConfigDbContext db, ILogger<AdminOptionsMarker> log, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return NotFound("tenant");
        }

        if (request.Status is not null)
        {
            if (request.Status is not ("active" or "suspended" or "closed"))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_request", "'status' must be active, suspended or closed.");
            }

            tenant.Status = request.Status;
        }

        if (request.Plan is not null)
        {
            var planCode = request.Plan.ToLowerInvariant();
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == planCode, ct).ConfigureAwait(false);
            if (plan is null)
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_request", "'plan' must be an existing plan code.");
            }

            var endpoints = await db.Endpoints.CountAsync(e => e.TenantId == tenantId, ct).ConfigureAwait(false);
            if (endpoints > plan.MaxEndpoints)
            {
                return Error(StatusCodes.Status409Conflict, "plan_downgrade_refused", $"The tenant has {endpoints} endpoints; plan '{plan.Code}' allows {plan.MaxEndpoints}. Remove endpoints first.");
            }

            tenant.PlanCode = plan.Code;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogTenantUpdated(log, tenantId, tenant.Status, tenant.PlanCode);
        return Results.Json(TenantView(tenant));
    }

    private static async Task<IResult> CreateApiKeyAsync(Guid tenantId, ConfigDbContext db, ILogger<AdminOptionsMarker> log, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct).ConfigureAwait(false))
        {
            return NotFound("tenant");
        }

        var key = "wgk_live_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var row = new ApiKeyRow
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            KeyHash = ApiKeyHasher.Hash(key),
            Prefix = key[..12],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogKeyCreated(log, tenantId, row.Id);
        return Results.Json(new { id = row.Id, prefix = row.Prefix, key, note = "Store this key now; it cannot be shown again." }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListApiKeysAsync(Guid tenantId, ConfigDbContext db, CancellationToken ct) =>
        Results.Json(await db.ApiKeys.AsNoTracking().Where(k => k.TenantId == tenantId).OrderBy(k => k.CreatedAt)
            .Select(k => new { id = k.Id, prefix = k.Prefix, createdAt = k.CreatedAt, revokedAt = k.RevokedAt }).ToListAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> RevokeApiKeyAsync(Guid tenantId, Guid keyId, ConfigDbContext db, ILogger<AdminOptionsMarker> log, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct).ConfigureAwait(false);
        if (key is null)
        {
            return NotFound("api key");
        }

        key.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogKeyRevoked(log, tenantId, keyId);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateEndpointAsync(
        Guid tenantId,
        CreateEndpoint request,
        ConfigDbContext db,
        ISecretProtector protector,
        IOptions<AdminOptions> options,
        ILogger<AdminOptionsMarker> log,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return NotFound("tenant");
        }

        var problem = ValidateUrl(request.Url, options.Value) ?? ValidateEventTypes(request.EventTypes);
        if (problem is not null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", problem);
        }

        var cap = await db.Plans.Where(p => p.Code == tenant.PlanCode).Select(p => p.MaxEndpoints).FirstAsync(ct).ConfigureAwait(false);
        if (await db.Endpoints.CountAsync(e => e.TenantId == tenantId, ct).ConfigureAwait(false) >= cap)
        {
            return Error(StatusCodes.Status409Conflict, "endpoint_limit", $"Plan '{tenant.PlanCode}' allows {cap} endpoints.");
        }

        var secret = SigningSecret.Generate().ToText();
        var stored = await protector.ProtectAsync(secret, ct).ConfigureAwait(false);
        var endpoint = new EndpointRow
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Url = request.Url!,
            SecretCiphertext = stored.Ciphertext,
            SecretKmsKeyId = stored.KeyId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Endpoints.Add(endpoint);
        foreach (var type in request.EventTypes!.Distinct(StringComparer.Ordinal))
        {
            db.Subscriptions.Add(new SubscriptionRow { Id = Guid.CreateVersion7(), TenantId = tenantId, EndpointId = endpoint.Id, EventType = type });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogEndpointCreated(log, tenantId, endpoint.Id);
        return Results.Json(
            new { id = endpoint.Id, url = endpoint.Url, status = endpoint.Status, eventTypes = request.EventTypes, secret, note = "Store the signing secret now; it cannot be shown again." },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListEndpointsAsync(Guid tenantId, ConfigDbContext db, CancellationToken ct)
    {
        var endpoints = await db.Endpoints.AsNoTracking().Where(e => e.TenantId == tenantId).OrderBy(e => e.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(ct).ConfigureAwait(false);
        return Results.Json(endpoints.Select(e => new
        {
            id = e.Id,
            url = e.Url,
            status = e.Status,
            eventTypes = subscriptions.Where(s => s.EndpointId == e.Id).Select(s => s.EventType).Order(StringComparer.Ordinal).ToList(),
        }));
    }

    private static async Task<IResult> UpdateEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        UpdateEndpoint request,
        ConfigDbContext db,
        IOptions<AdminOptions> options,
        ILogger<AdminOptionsMarker> log,
        CancellationToken ct)
    {
        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId && e.TenantId == tenantId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return NotFound("endpoint");
        }

        if (request.Url is not null)
        {
            var problem = ValidateUrl(request.Url, options.Value);
            if (problem is not null)
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_request", problem);
            }

            endpoint.Url = request.Url;
        }

        if (request.Status is not null)
        {
            if (request.Status is not ("active" or "disabled"))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_request", "'status' must be active or disabled.");
            }

            endpoint.Status = request.Status;
        }

        endpoint.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogEndpointUpdated(log, tenantId, endpointId, endpoint.Status);
        return Results.Json(new { id = endpoint.Id, url = endpoint.Url, status = endpoint.Status });
    }

    private static async Task<IResult> SetSubscriptionsAsync(
        Guid tenantId,
        Guid endpointId,
        SetSubscriptions request,
        ConfigDbContext db,
        ILogger<AdminOptionsMarker> log,
        CancellationToken ct)
    {
        if (!await db.Endpoints.AnyAsync(e => e.Id == endpointId && e.TenantId == tenantId, ct).ConfigureAwait(false))
        {
            return NotFound("endpoint");
        }

        var problem = ValidateEventTypes(request.EventTypes);
        if (problem is not null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", problem);
        }

        var wanted = request.EventTypes!.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var existing = await db.Subscriptions.Where(s => s.EndpointId == endpointId).ToListAsync(ct).ConfigureAwait(false);
        db.Subscriptions.RemoveRange(existing.Where(s => !wanted.Contains(s.EventType)));
        foreach (var type in wanted.Except(existing.Select(s => s.EventType)))
        {
            db.Subscriptions.Add(new SubscriptionRow { Id = Guid.CreateVersion7(), TenantId = tenantId, EndpointId = endpointId, EventType = type });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogSubscriptionsSet(log, tenantId, endpointId, wanted.Count);
        return Results.Json(new { eventTypes = wanted.Order(StringComparer.Ordinal).ToList() });
    }

    private static string? ValidateUrl(string? url, AdminOptions options) =>
        url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? "'url' is required and must be an absolute URL."
            : EndpointUrlValidator.Validate(uri, options.AllowInsecureHttp, options.AllowPrivateNetworks);

    private static string? ValidateEventTypes(IReadOnlyList<string>? types) =>
        types is null || types.Count == 0 || types.Count > 100
            ? "'eventTypes' is required (1 to 100 entries)."
            : types.Any(t => t != "*" && (t.Length is 0 or > 128 || !EventTypePattern().IsMatch(t)))
                ? "Each event type must be '*' or 1-128 characters of letters, digits, '.', '_', ':' or '-'."
                : null;

    private static object TenantView(TenantRow t) => new { id = t.Id, name = t.Name, status = t.Status, plan = t.PlanCode, createdAt = t.CreatedAt };

    private static IResult NotFound(string what) => Error(StatusCodes.Status404NotFound, "not_found", $"The {what} does not exist.");

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EventTypePattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin request rejected: missing or invalid admin key (from {RemoteAddress})")]
    private static partial void LogAdminRejected(ILogger logger, string? remoteAddress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant {TenantId} created on plan {Plan}")]
    private static partial void LogTenantCreated(ILogger logger, Guid tenantId, string plan);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant {TenantId} updated: status {Status}, plan {Plan}")]
    private static partial void LogTenantUpdated(ILogger logger, Guid tenantId, string status, string plan);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key {KeyId} created for tenant {TenantId}")]
    private static partial void LogKeyCreated(ILogger logger, Guid tenantId, Guid keyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key {KeyId} of tenant {TenantId} revoked")]
    private static partial void LogKeyRevoked(ILogger logger, Guid tenantId, Guid keyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint {EndpointId} created for tenant {TenantId}")]
    private static partial void LogEndpointCreated(ILogger logger, Guid tenantId, Guid endpointId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint {EndpointId} of tenant {TenantId} updated: status {Status}")]
    private static partial void LogEndpointUpdated(ILogger logger, Guid tenantId, Guid endpointId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint {EndpointId} of tenant {TenantId} now has {Count} subscriptions")]
    private static partial void LogSubscriptionsSet(ILogger logger, Guid tenantId, Guid endpointId, int count);
}

/// <summary>Category for the admin API's log lines.</summary>
public sealed class AdminOptionsMarker;

public sealed record CreateTenant(string? Name, string? Plan);

public sealed record UpdateTenant(string? Status, string? Plan);

public sealed record CreateEndpoint(string? Url, IReadOnlyList<string>? EventTypes);

public sealed record UpdateEndpoint(string? Url, string? Status);

public sealed record SetSubscriptions(IReadOnlyList<string>? EventTypes);

/// <summary>Response shapes, used for the OpenAPI document and the Swagger page.</summary>
public sealed record ErrorBody(ErrorDetail Error);

public sealed record ErrorDetail(string Code, string Message);

public sealed record TenantView(Guid Id, string Name, string Status, string Plan, DateTimeOffset CreatedAt);

public sealed record ApiKeyCreated(Guid Id, string Prefix, string Key, string Note);

public sealed record ApiKeyInfo(Guid Id, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record EndpointCreated(Guid Id, string Url, string Status, IReadOnlyList<string> EventTypes, string Secret, string Note);

public sealed record EndpointInfo(Guid Id, string Url, string Status, IReadOnlyList<string> EventTypes);

public sealed record EndpointStatusView(Guid Id, string Url, string Status);

public sealed record SubscriptionsView(IReadOnlyList<string> EventTypes);
