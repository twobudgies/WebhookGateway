using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Ingest.Limits;

/// <param name="Allowed">True when the event may be accepted.</param>
/// <param name="Limit">Sustained limit in events per second.</param>
/// <param name="Remaining">Tokens left after this call.</param>
/// <param name="RetryAfter">When not allowed, the earliest time a retry can succeed.</param>
public readonly record struct RateLimitResult(bool Allowed, int Limit, int Remaining, TimeSpan RetryAfter);

/// <summary>Per-tenant accept rate (events/second with burst). Shared state lives behind the implementation.</summary>
public interface IRateLimiter
{
    ValueTask<RateLimitResult> TryAcquireAsync(TenantId tenantId, PlanLimits limits, CancellationToken cancellationToken);
}
