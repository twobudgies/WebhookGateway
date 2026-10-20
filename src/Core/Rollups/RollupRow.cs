using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Core.Rollups;

/// <summary>Identifies one rollup row: tenant, endpoint (or <see cref="AllEndpoints"/>) and the minute it covers (UTC).</summary>
public readonly record struct RollupKey(TenantId TenantId, string EndpointKey, DateTimeOffset Minute)
{
    /// <summary>The endpoint component of the tenant-wide row.</summary>
    public const string AllEndpoints = "*";
}

/// <summary>Counters and histograms for one rollup key; flushed to DynamoDB with atomic adds.</summary>
public sealed class RollupRow
{
    public long Created { get; set; }

    public long Attempts { get; set; }

    public long Succeeded { get; set; }

    public long Retried { get; set; }

    public long Failed { get; set; }

    public long Exhausted { get; set; }

    public long Timeouts { get; set; }

    public LatencyHistogram ResponseTime { get; } = new();

    public LatencyHistogram QueueWait { get; } = new();

    /// <summary>Sum of response times in milliseconds: how much connection time this endpoint used (Little's law).</summary>
    public long ConnectionMs => ResponseTime.SumMs;

    public void Merge(RollupRow other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Created += other.Created;
        Attempts += other.Attempts;
        Succeeded += other.Succeeded;
        Retried += other.Retried;
        Failed += other.Failed;
        Exhausted += other.Exhausted;
        Timeouts += other.Timeouts;
        ResponseTime.Merge(other.ResponseTime);
        QueueWait.Merge(other.QueueWait);
    }
}
