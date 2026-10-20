using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.FanOut.Routing;

/// <summary>Cached read model of tenant configuration (the config database is its source of truth).</summary>
public interface ISubscriptionStore
{
    /// <returns>Routing for the tenant, or null when the tenant is unknown or no longer active.</returns>
    ValueTask<TenantRouting?> GetRoutingAsync(TenantId tenantId, CancellationToken cancellationToken);
}
