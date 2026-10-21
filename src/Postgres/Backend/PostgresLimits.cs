using System.Globalization;
using Npgsql;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;

namespace WebhookGateway.Postgres.Backend;

/// <summary>
/// Idempotency keys in PostgreSQL: durable across restarts and shared by every Ingest instance without Valkey.
/// One statement claims a free or expired key; otherwise the existing event id is returned.
/// </summary>
public sealed class PostgresIdempotencyStore(NpgsqlDataSource db) : IIdempotencyStore
{
    public async ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken)
    {
        await using var claim = db.CreateCommand(
            """
            INSERT INTO webhook.idempotency_keys (tenant_id, key, event_id, expires_at)
            VALUES (@t, @k, @e, now() + make_interval(secs => @window))
            ON CONFLICT (tenant_id, key) DO UPDATE SET event_id = EXCLUDED.event_id, expires_at = EXCLUDED.expires_at
            WHERE webhook.idempotency_keys.expires_at <= now()
            RETURNING event_id
            """);
        claim.Parameters.AddWithValue("t", tenantId.Value);
        claim.Parameters.AddWithValue("k", key);
        claim.Parameters.AddWithValue("e", eventId.Value);
        claim.Parameters.AddWithValue("window", window.TotalSeconds);
        if (await claim.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string)
        {
            return new IdempotencyClaim(true, null);
        }

        await using var read = db.CreateCommand("SELECT event_id FROM webhook.idempotency_keys WHERE tenant_id = @t AND key = @k");
        read.Parameters.AddWithValue("t", tenantId.Value);
        read.Parameters.AddWithValue("k", key);
        return await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing
            ? new IdempotencyClaim(false, EventId.Parse(existing))
            : new IdempotencyClaim(true, null);
    }

    public async ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand("DELETE FROM webhook.idempotency_keys WHERE tenant_id = @t AND key = @k");
        command.Parameters.AddWithValue("t", tenantId.Value);
        command.Parameters.AddWithValue("k", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Monthly accepted-event counter per tenant in PostgreSQL (durable, shared).</summary>
public sealed class PostgresQuotaCounter(NpgsqlDataSource db) : IQuotaCounter
{
    public async ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(
            "INSERT INTO webhook.usage_counters (tenant_id, month, accepted) VALUES (@t, @m, 1) ON CONFLICT (tenant_id, month) DO UPDATE SET accepted = webhook.usage_counters.accepted + 1 RETURNING accepted");
        command.Parameters.AddWithValue("t", tenantId.Value);
        command.Parameters.AddWithValue("m", Month(now));
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand("UPDATE webhook.usage_counters SET accepted = GREATEST(0, accepted - 1) WHERE tenant_id = @t AND month = @m");
        command.Parameters.AddWithValue("t", tenantId.Value);
        command.Parameters.AddWithValue("m", Month(now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Month(DateTimeOffset now) => now.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);
}
