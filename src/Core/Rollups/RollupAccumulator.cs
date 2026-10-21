using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Core.Rollups;

/// <summary>One delivery attempt as seen by the Dispatcher.</summary>
public readonly record struct AttemptSample(
    TenantId TenantId,
    EndpointId EndpointId,
    DeliveryOutcome Outcome,
    long QueueWaitMs,
    long ResponseMs,
    bool TimedOut,
    DateTimeOffset At);

/// <summary>
/// In-memory counters kept by each worker and drained every few seconds (docs/architecture.md section 7).
/// Write volume to the store depends on the number of active (tenant, endpoint) pairs, not on the event rate.
/// Each drain produces one row per endpoint and minute plus a tenant-wide row (<see cref="RollupKey.AllEndpoints"/>).
/// </summary>
public sealed class RollupAccumulator
{
    private readonly Lock _gate = new();
    private Dictionary<RollupKey, RollupRow> _rows = [];

    public void RecordCreated(TenantId tenantId, EndpointId endpointId, DateTimeOffset at)
    {
        lock (_gate)
        {
            Row(tenantId, endpointId.Value, at).Created++;
        }
    }

    public void RecordAttempt(AttemptSample sample)
    {
        lock (_gate)
        {
            var row = Row(sample.TenantId, sample.EndpointId.Value, sample.At);
            row.Attempts++;
            switch (sample.Outcome)
            {
                case DeliveryOutcome.Succeeded:
                    row.Succeeded++;
                    break;
                case DeliveryOutcome.Retry:
                    row.Retried++;
                    break;
                case DeliveryOutcome.Failed:
                    row.Failed++;
                    break;
                case DeliveryOutcome.Exhausted:
                    row.Exhausted++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sample), sample.Outcome, "Unknown outcome.");
            }

            if (sample.TimedOut)
            {
                row.Timeouts++;
            }

            row.ResponseTime.Add(sample.ResponseMs);
            row.QueueWait.Add(sample.QueueWaitMs);
        }
    }

    /// <summary>Takes everything recorded so far (endpoint rows and tenant-wide rows) and starts again from empty.</summary>
    public IReadOnlyDictionary<RollupKey, RollupRow> Drain()
    {
        Dictionary<RollupKey, RollupRow> taken;
        lock (_gate)
        {
            taken = _rows;
            _rows = [];
        }

        var result = new Dictionary<RollupKey, RollupRow>(taken);
        foreach (var (key, row) in taken)
        {
            var tenantKey = key with { EndpointKey = RollupKey.AllEndpoints };
            if (!result.TryGetValue(tenantKey, out var tenantRow))
            {
                result[tenantKey] = tenantRow = new RollupRow();
            }

            tenantRow.Merge(row);
        }

        return result;
    }

    private RollupRow Row(TenantId tenantId, string endpointKey, DateTimeOffset at)
    {
        var minute = new DateTimeOffset(at.UtcDateTime.Ticks - (at.UtcDateTime.Ticks % TimeSpan.TicksPerMinute), TimeSpan.Zero);
        var key = new RollupKey(tenantId, endpointKey, minute);
        if (!_rows.TryGetValue(key, out var row))
        {
            _rows[key] = row = new RollupRow();
        }

        return row;
    }
}
