using StackExchange.Redis;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Valkey;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>, Valkey on 6379); skipped when it is not reachable.</summary>
public sealed class ValkeyTests
{
    private static async Task<IConnectionMultiplexer?> ConnectAsync()
    {
        try
        {
            var options = ConfigurationOptions.Parse("localhost:6379");
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 1000;
            return await ConnectionMultiplexer.ConnectAsync(options);
        }
        catch (RedisConnectionException)
        {
            return null;
        }
    }

    [Fact]
    public async Task The_shared_bucket_allows_the_burst_then_rejects_with_a_retry_time()
    {
        await using var valkey = (ConnectionMultiplexer?)await ConnectAsync();
        if (valkey is null)
        {
            return;
        }

        var limiter = new ValkeyRateLimiter(valkey);
        var limits = new PlanLimits(EventsPerSecond: 1, BurstCapacity: 3, MaxPayloadBytes: 1024, TimeSpan.FromHours(1), 1000);
        var tenant = TenantId.New();

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await limiter.TryAcquireAsync(tenant, limits, CancellationToken.None)).Allowed);
        }

        var rejected = await limiter.TryAcquireAsync(tenant, limits, CancellationToken.None);

        Assert.False(rejected.Allowed);
        Assert.InRange(rejected.RetryAfter, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1.1));
    }

    [Fact]
    public async Task The_first_idempotency_claim_wins_and_a_release_frees_the_key()
    {
        await using var valkey = (ConnectionMultiplexer?)await ConnectAsync();
        if (valkey is null)
        {
            return;
        }

        var store = new ValkeyIdempotencyStore(valkey);
        var tenant = TenantId.New();
        var first = EventId.New();

        var a = await store.TryClaimAsync(tenant, "k1", first, TimeSpan.FromMinutes(5), CancellationToken.None);
        var b = await store.TryClaimAsync(tenant, "k1", EventId.New(), TimeSpan.FromMinutes(5), CancellationToken.None);
        await store.ReleaseAsync(tenant, "k1", CancellationToken.None);
        var c = await store.TryClaimAsync(tenant, "k1", EventId.New(), TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(a.Claimed);
        Assert.False(b.Claimed);
        Assert.Equal(first, b.ExistingEventId);
        Assert.True(c.Claimed);
    }

    [Fact]
    public async Task The_monthly_counter_counts_up_and_back_down()
    {
        await using var valkey = (ConnectionMultiplexer?)await ConnectAsync();
        if (valkey is null)
        {
            return;
        }

        var counter = new ValkeyQuotaCounter(valkey);
        var tenant = TenantId.New();
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        await counter.IncrementAsync(tenant, now, CancellationToken.None);
        var two = await counter.IncrementAsync(tenant, now, CancellationToken.None);
        await counter.DecrementAsync(tenant, now, CancellationToken.None);
        var again = await counter.IncrementAsync(tenant, now, CancellationToken.None);

        Assert.Equal(2, two);
        Assert.Equal(2, again);
    }
}
