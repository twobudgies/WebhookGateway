using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.UnitTests.Ingest;

public sealed class LocalRateLimiterTests
{
    private static readonly PlanLimits Small = new(EventsPerSecond: 2, BurstCapacity: 2, MaxPayloadBytes: 1024, IdempotencyWindow: TimeSpan.FromHours(1), MonthlyQuota: 1000);

    [Fact]
    public async Task Tenants_have_independent_buckets()
    {
        var limiter = new LocalRateLimiter(new FakeTimeProvider());
        var a = TenantId.New();
        var b = TenantId.New();

        await limiter.TryAcquireAsync(a, Small, CancellationToken.None);
        await limiter.TryAcquireAsync(a, Small, CancellationToken.None);
        var aThird = await limiter.TryAcquireAsync(a, Small, CancellationToken.None);
        var bFirst = await limiter.TryAcquireAsync(b, Small, CancellationToken.None);

        Assert.False(aThird.Allowed);
        Assert.True(bFirst.Allowed);
    }

    [Fact]
    public async Task Reports_limit_and_remaining()
    {
        var limiter = new LocalRateLimiter(new FakeTimeProvider());
        var tenant = TenantId.New();

        var first = await limiter.TryAcquireAsync(tenant, Small, CancellationToken.None);

        Assert.Equal(2, first.Limit);
        Assert.Equal(1, first.Remaining);
    }
}
