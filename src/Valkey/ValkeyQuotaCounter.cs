using System.Globalization;
using StackExchange.Redis;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;

namespace WebhookGateway.Valkey;

/// <summary><c>INCR</c> per tenant and month; the key expires 40 days after it is first written (docs/tenancy-and-limits.md section 3).</summary>
public sealed class ValkeyQuotaCounter(IConnectionMultiplexer valkey) : IQuotaCounter
{
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(40);

    public async ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var db = valkey.GetDatabase();
        var key = Key(tenantId, now);
        var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, KeyLifetime).ConfigureAwait(false);
        }

        return count;
    }

    public async ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await valkey.GetDatabase().StringDecrementAsync(Key(tenantId, now)).ConfigureAwait(false);

    private static RedisKey Key(TenantId tenantId, DateTimeOffset now) =>
        $"quota:{tenantId.Value}:{now.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture)}";
}
