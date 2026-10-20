using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Ingest.Limits;

/// <summary>
/// In-process limiter. It is exact for one task; with several tasks each enforces its own bucket, so the
/// shared (Valkey) limiter replaces it in production and this one stays as the degraded-mode fallback
/// (docs/architecture.md section 5).
/// </summary>
public sealed class LocalRateLimiter(TimeProvider timeProvider) : IRateLimiter
{
    private readonly ConcurrentDictionary<TenantId, TokenBucket> _buckets = new();

    public ValueTask<RateLimitResult> TryAcquireAsync(TenantId tenantId, PlanLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var now = timeProvider.GetTimestamp();
        var bucket = _buckets.GetOrAdd(
            tenantId,
            _ => new TokenBucket(limits.BurstCapacity, limits.EventsPerSecond, now));

        var (taken, remaining, retryAfter) = bucket.TryTake(now, timeProvider.TimestampFrequency);
        return ValueTask.FromResult(new RateLimitResult(taken, limits.EventsPerSecond, remaining, retryAfter));
    }
}
