using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Limits;

/// <summary>Single-process store for tests and local runs; the Valkey store is shared across Ingest tasks.</summary>
public sealed class InMemoryIdempotencyStore(TimeProvider clock) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(TenantId, string), (EventId EventId, DateTimeOffset Expires)> _claims = new();

    public ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var entry = _claims.AddOrUpdate(
            (tenantId, key),
            _ => (eventId, now + window),
            (_, existing) => existing.Expires <= now ? (eventId, now + window) : existing);
        return ValueTask.FromResult(entry.EventId == eventId ? new IdempotencyClaim(true, null) : new IdempotencyClaim(false, entry.EventId));
    }

    public ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken)
    {
        _claims.TryRemove((tenantId, key), out _);
        return ValueTask.CompletedTask;
    }
}
