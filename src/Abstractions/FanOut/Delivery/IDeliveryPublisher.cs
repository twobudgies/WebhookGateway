using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Delivery;

/// <summary>Sends delivery messages to the tier's SQS queue. Throws if any message could not be stored.</summary>
public interface IDeliveryPublisher
{
    ValueTask PublishAsync(PlanTier tier, IReadOnlyList<DeliveryMessage> deliveries, CancellationToken cancellationToken);
}
