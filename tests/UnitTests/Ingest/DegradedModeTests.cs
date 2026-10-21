using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.UnitTests.Ingest;

public sealed class DegradedModeTests
{
    private static readonly PlanLimits Limits = PlanLimits.For(PlanTier.Basic);

    [Fact]
    public async Task The_rate_limiter_falls_back_to_the_local_one_when_the_shared_one_throws()
    {
        var limiter = new ResilientRateLimiter(new ThrowingLimiter(), new LocalRateLimiter(new FakeTimeProvider()), NullLogger<ResilientRateLimiter>.Instance);

        var result = await limiter.TryAcquireAsync(TenantId.New(), Limits, CancellationToken.None);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task Idempotency_and_quota_checks_are_skipped_not_failed_when_the_store_throws()
    {
        var idempotency = new ResilientIdempotencyStore(new ThrowingStore(), NullLogger<ResilientIdempotencyStore>.Instance);
        var quota = new ResilientQuotaCounter(new ThrowingStore(), NullLogger<ResilientQuotaCounter>.Instance);

        var claim = await idempotency.TryClaimAsync(TenantId.New(), "k", EventId.New(), TimeSpan.FromHours(1), CancellationToken.None);
        var count = await quota.IncrementAsync(TenantId.New(), DateTimeOffset.UtcNow, CancellationToken.None);
        await quota.DecrementAsync(TenantId.New(), DateTimeOffset.UtcNow, CancellationToken.None);
        await idempotency.ReleaseAsync(TenantId.New(), "k", CancellationToken.None);

        Assert.True(claim.Claimed);
        Assert.Equal(0, count);
    }

    private sealed class ThrowingLimiter : IRateLimiter
    {
        public ValueTask<RateLimitResult> TryAcquireAsync(TenantId tenantId, PlanLimits limits, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("valkey down");
    }

    private sealed class ThrowingStore : IIdempotencyStore, IQuotaCounter
    {
        public ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("valkey down");

        public ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("valkey down");

        public ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("valkey down");

        public ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("valkey down");
    }
}
