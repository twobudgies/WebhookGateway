using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.FanOut.Routing;

/// <summary>Store for tests and a no-database local run.</summary>
public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly ConcurrentDictionary<TenantId, TenantRouting> _routing = new();

    public void Set(TenantId tenantId, TenantRouting routing) => _routing[tenantId] = routing;

    public ValueTask<TenantRouting?> GetRoutingAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_routing.GetValueOrDefault(tenantId));
}
