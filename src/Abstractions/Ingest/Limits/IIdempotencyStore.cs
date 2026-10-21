using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Limits;

/// <param name="Claimed">True when this call recorded the key (a new event); false when the key was already used.</param>
/// <param name="ExistingEventId">When not claimed, the event id returned for the first request with this key.</param>
public readonly record struct IdempotencyClaim(bool Claimed, EventId? ExistingEventId);

/// <summary>Remembers <c>Idempotency-Key</c> values per tenant for the plan's window so a repeated request returns the same event.</summary>
public interface IIdempotencyStore
{
    ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>Forgets a claim whose event was not stored, so a retry with the same key is not answered with a phantom event.</summary>
    ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken);
}
