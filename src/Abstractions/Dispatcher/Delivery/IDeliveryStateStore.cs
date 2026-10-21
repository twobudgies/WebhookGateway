using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Dispatcher.Delivery;

public interface IDeliveryStateStore
{
    ValueTask<DeliveryStateRecord?> GetAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken);

    ValueTask PutAsync(DeliveryStateRecord record, CancellationToken cancellationToken);

    /// <summary>Removes the row after a later attempt succeeded.</summary>
    ValueTask DeleteAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken);
}
