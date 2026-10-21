using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Postgres;

/// <summary>
/// API key to tenant and effective limits from the config database, cached (docs/database.md). Effective limit =
/// per-tenant override if present, otherwise the plan value. Revoked keys and unknown keys resolve to null.
/// </summary>
public sealed partial class PostgresApiKeyStore : IApiKeyStore
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresApiKeyStore> _logger;
    private readonly TtlCache<string, TenantContext> _cache;

    public PostgresApiKeyStore(NpgsqlDataSource db, TimeProvider clock, IOptions<PostgresOptions> options, ILogger<PostgresApiKeyStore> logger)
    {
        _db = db;
        _logger = logger;
        _cache = new TtlCache<string, TenantContext>(clock, options.Value.CacheTtl, options.Value.NegativeCacheTtl);
    }

    public ValueTask<TenantContext?> FindByKeyHashAsync(string keyHash, CancellationToken cancellationToken) =>
        _cache.GetOrLoadAsync(keyHash, LoadAsync, cancellationToken);

    private async ValueTask<TenantContext?> LoadAsync(string keyHash, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Guid tenantId;
        string status;
        string tier;
        int acceptRate;
        int burst;
        int maxPayload;
        TimeSpan idempotency;
        long quota;
        await using (var command = new NpgsqlCommand(
            """
            SELECT t.id, t.status, p.queue_tier, p.accept_rate, p.burst_capacity, p.max_payload_bytes, p.idempotency_window, p.monthly_quota
            FROM webhook.api_keys k
            JOIN webhook.tenants t ON t.id = k.tenant_id
            JOIN webhook.plans p ON p.code = t.plan_code
            WHERE k.key_hash = @hash AND k.revoked_at IS NULL
            """,
            connection))
        {
            command.Parameters.AddWithValue("hash", keyHash);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            tenantId = reader.GetGuid(0);
            status = reader.GetString(1);
            tier = reader.GetString(2);
            acceptRate = reader.GetInt32(3);
            burst = reader.GetInt32(4);
            maxPayload = reader.GetInt32(5);
            idempotency = reader.GetFieldValue<TimeSpan>(6);
            quota = reader.GetInt64(7);
        }

        await using (var overrides = new NpgsqlCommand("SELECT field, value FROM webhook.tenant_overrides WHERE tenant_id = @id", connection))
        {
            overrides.Parameters.AddWithValue("id", tenantId);
            await using var reader = await overrides.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var (field, value) = (reader.GetString(0), reader.GetString(1));
                switch (field)
                {
                    case "accept_rate": acceptRate = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "burst_capacity": burst = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "max_payload_bytes": maxPayload = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "monthly_quota": quota = long.Parse(value, CultureInfo.InvariantCulture); break;
                    case "idempotency_window": idempotency = TimeSpan.Parse(value, CultureInfo.InvariantCulture); break;
                    default: LogUnknownOverride(_logger, tenantId, field); break;
                }
            }
        }

        var plan = Enum.Parse<PlanTier>(tier, ignoreCase: true);
        var tenantStatus = Enum.Parse<TenantStatus>(status, ignoreCase: true);
        return new TenantContext(ConfigIds.Tenant(tenantId), plan, tenantStatus, new PlanLimits(acceptRate, burst, maxPayload, idempotency, quota));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant {TenantId} has an override for unknown field '{Field}'; ignored")]
    private static partial void LogUnknownOverride(ILogger logger, Guid tenantId, string field);
}
