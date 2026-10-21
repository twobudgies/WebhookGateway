using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Limits;

/// <summary>Accepted events per tenant and calendar month (UTC). Counted for billing even when the quota is not enforced.</summary>
public interface IQuotaCounter
{
    /// <returns>The month's count including this event.</returns>
    ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Takes one back: the event was a duplicate, was over quota, or could not be stored.</summary>
    ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken);
}
