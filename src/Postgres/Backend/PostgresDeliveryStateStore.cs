using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.Postgres.Backend;

public sealed class BackendOptions
{
    public const string SectionName = "Backend:Postgres";

    /// <summary>How long a delivery_state row lives (D34/D53: plan retention arrives with the plan lookup).</summary>
    public TimeSpan DeliveryStateRetention { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan RollupMinuteRetention { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan ArchiveRetention { get; init; } = TimeSpan.FromDays(30);
}

/// <summary><c>webhook.delivery_state</c>: only non-success deliveries, plus the due index used by the retry scheduler.</summary>
public sealed partial class PostgresDeliveryStateStore(
    NpgsqlDataSource db,
    TimeProvider clock,
    IOptions<BackendOptions> options,
    ILogger<PostgresDeliveryStateStore> logger) : IDeliveryStateStore, IDueDeliveryIndex
{
    private const string Columns =
        "tenant_id, delivery_id, endpoint_id, event_id, event_type, occurred_at, status, attempt, tier, next_attempt_at, last_status_code, last_error, payload, payload_ref, created_at, updated_at";

    private readonly BackendOptions _options = options.Value;

    public async ValueTask<DeliveryStateRecord?> GetAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand($"SELECT {Columns} FROM webhook.delivery_state WHERE tenant_id = @t AND delivery_id = @d");
        command.Parameters.AddWithValue("t", tenantId.Value);
        command.Parameters.AddWithValue("d", deliveryId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async ValueTask PutAsync(DeliveryStateRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var command = db.CreateCommand(
            $"""
            INSERT INTO webhook.delivery_state ({Columns}, expires_at)
            VALUES (@tenant, @delivery, @endpoint, @event, @type, @occurred, @status, @attempt, @tier, @next, @code, @error, @payload, @ref, @created, @updated, @expires)
            ON CONFLICT (tenant_id, delivery_id) DO UPDATE SET
                status = EXCLUDED.status, attempt = EXCLUDED.attempt, tier = EXCLUDED.tier, next_attempt_at = EXCLUDED.next_attempt_at,
                last_status_code = EXCLUDED.last_status_code, last_error = EXCLUDED.last_error, payload = EXCLUDED.payload,
                payload_ref = EXCLUDED.payload_ref, updated_at = EXCLUDED.updated_at, expires_at = EXCLUDED.expires_at
            """);
        var p = command.Parameters;
        p.AddWithValue("tenant", record.TenantId.Value);
        p.AddWithValue("delivery", record.DeliveryId.Value);
        p.AddWithValue("endpoint", record.EndpointId.Value);
        p.AddWithValue("event", record.EventId.Value);
        p.AddWithValue("type", record.EventType);
        p.AddWithValue("occurred", record.OccurredAt);
        p.AddWithValue("status", record.Status.ToString());
        p.AddWithValue("attempt", record.Attempt);
        p.AddWithValue("tier", record.Tier.ToString());
        p.AddWithValue("next", (object?)record.NextAttemptAt ?? DBNull.Value);
        p.AddWithValue("code", (object?)record.LastStatusCode ?? DBNull.Value);
        p.AddWithValue("error", (object?)record.LastError ?? DBNull.Value);
        p.AddWithValue("payload", record.Payload is { } payload ? payload.GetRawText() : DBNull.Value);
        p.AddWithValue("ref", (object?)record.PayloadRef ?? DBNull.Value);
        p.AddWithValue("created", record.CreatedAt);
        p.AddWithValue("updated", record.UpdatedAt);
        p.AddWithValue("expires", clock.GetUtcNow().Add(_options.DeliveryStateRetention));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogStored(logger, record.TenantId.Value, record.DeliveryId.Value, record.Status, record.Attempt);
    }

    public async ValueTask DeleteAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand("DELETE FROM webhook.delivery_state WHERE tenant_id = @t AND delivery_id = @d");
        command.Parameters.AddWithValue("t", tenantId.Value);
        command.Parameters.AddWithValue("d", deliveryId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogDeleted(logger, tenantId.Value, deliveryId.Value);
    }

    public async ValueTask<IReadOnlyList<DeliveryStateRecord>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(
            $"SELECT {Columns} FROM webhook.delivery_state WHERE status = 'Scheduled' AND next_attempt_at <= @now ORDER BY next_attempt_at LIMIT @limit");
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);
        var due = new List<DeliveryStateRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            due.Add(Read(reader));
        }

        return due;
    }

    private static DeliveryStateRecord Read(NpgsqlDataReader r) =>
        new(
            TenantId.Parse(r.GetString(0)),
            DeliveryId.Parse(r.GetString(1)),
            EndpointId.Parse(r.GetString(2)),
            EventId.Parse(r.GetString(3)),
            r.GetString(4),
            r.GetFieldValue<DateTimeOffset>(5),
            Enum.Parse<DeliveryStatus>(r.GetString(6)),
            r.GetInt32(7),
            r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
            r.IsDBNull(10) ? null : r.GetInt32(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : JsonDocument.Parse(r.GetString(12)).RootElement.Clone(),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.GetFieldValue<DateTimeOffset>(14),
            r.GetFieldValue<DateTimeOffset>(15),
            Enum.Parse<PlanTier>(r.GetString(8)));

    [LoggerMessage(Level = LogLevel.Debug, Message = "delivery_state stored {DeliveryId} of tenant {TenantId}: {Status}, attempt {Attempt}")]
    private static partial void LogStored(ILogger logger, string tenantId, string deliveryId, DeliveryStatus status, int attempt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "delivery_state deleted {DeliveryId} of tenant {TenantId}")]
    private static partial void LogDeleted(ILogger logger, string tenantId, string deliveryId);
}
