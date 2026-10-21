using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Dispatcher.Delivery;

public sealed class InMemoryDeliveryStateStore : IDeliveryStateStore, IDueDeliveryIndex
{
    private readonly ConcurrentDictionary<(TenantId, DeliveryId), DeliveryStateRecord> _rows = new();

    public IReadOnlyCollection<DeliveryStateRecord> Rows => _rows.Values.ToArray();

    public ValueTask<DeliveryStateRecord?> GetAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_rows.GetValueOrDefault((tenantId, deliveryId)));

    public ValueTask PutAsync(DeliveryStateRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        _rows[(record.TenantId, record.DeliveryId)] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        _rows.TryRemove((tenantId, deliveryId), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<DeliveryStateRecord>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DeliveryStateRecord>>(
            [.. _rows.Values.Where(r => r.Status == WebhookGateway.Contracts.DeliveryStatus.Scheduled && r.NextAttemptAt <= now).Take(limit)]);
}
