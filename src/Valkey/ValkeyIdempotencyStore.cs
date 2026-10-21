using StackExchange.Redis;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;

namespace WebhookGateway.Valkey;

/// <summary><c>SET NX PX</c> per tenant and key; the value is the event id of the first request.</summary>
public sealed class ValkeyIdempotencyStore(IConnectionMultiplexer valkey) : IIdempotencyStore
{
    public async ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken)
    {
        var db = valkey.GetDatabase();
        var redisKey = Key(tenantId, key);
        if (await db.StringSetAsync(redisKey, eventId.Value, window, When.NotExists).ConfigureAwait(false))
        {
            return new IdempotencyClaim(true, null);
        }

        var existing = await db.StringGetAsync(redisKey).ConfigureAwait(false);
        // The key expired between the two calls: treat it as new.
        return existing.IsNull
            ? new IdempotencyClaim(true, null)
            : new IdempotencyClaim(false, EventId.Parse(existing.ToString()));
    }

    public async ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken) =>
        await valkey.GetDatabase().KeyDeleteAsync(Key(tenantId, key)).ConfigureAwait(false);

    private static RedisKey Key(TenantId tenantId, string key) => $"idem:{tenantId.Value}:{key}";
}
