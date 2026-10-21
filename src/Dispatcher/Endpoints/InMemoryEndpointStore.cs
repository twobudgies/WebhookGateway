using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Dispatcher.Endpoints;

/// <summary>Store for tests and a no-database local run.</summary>
public sealed class InMemoryEndpointStore : IEndpointStore
{
    private readonly ConcurrentDictionary<(TenantId, EndpointId), EndpointTarget> _targets = new();

    public void Set(TenantId tenantId, EndpointTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _targets[(tenantId, target.EndpointId)] = target;
    }

    public ValueTask<EndpointTarget?> GetAsync(TenantId tenantId, EndpointId endpointId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_targets.GetValueOrDefault((tenantId, endpointId)));
}
