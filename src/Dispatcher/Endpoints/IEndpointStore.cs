using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Dispatcher.Endpoints;

/// <summary>Cached read model of endpoint configuration (the config database is the source of truth).</summary>
public interface IEndpointStore
{
    /// <returns>The target, or null when the endpoint no longer exists or is disabled.</returns>
    ValueTask<EndpointTarget?> GetAsync(TenantId tenantId, EndpointId endpointId, CancellationToken cancellationToken);
}
