using System.Collections.Concurrent;

namespace WebhookGateway.Postgres;

/// <summary>Small time-based cache for configuration reads; a missing value is cached for a shorter time.</summary>
internal sealed class TtlCache<TKey, TValue>(TimeProvider clock, TimeSpan ttl, TimeSpan negativeTtl)
    where TKey : notnull
    where TValue : class
{
    private readonly ConcurrentDictionary<TKey, (TValue? Value, DateTimeOffset Expires)> _entries = new();

    public async ValueTask<TValue?> GetOrLoadAsync(TKey key, Func<TKey, CancellationToken, ValueTask<TValue?>> load, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(key, out var hit) && hit.Expires > now)
        {
            return hit.Value;
        }

        var value = await load(key, cancellationToken).ConfigureAwait(false);
        _entries[key] = (value, now + (value is null ? negativeTtl : ttl));
        return value;
    }
}
