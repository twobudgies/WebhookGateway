using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Delivery;

/// <summary>Handles one delivery message. An exception means "not done": the message is not acknowledged and is redelivered.</summary>
public interface IDeliveryProcessor
{
    ValueTask<DeliveryAction> ProcessAsync(DeliveryMessage message, PlanTier tier, CancellationToken cancellationToken);
}
