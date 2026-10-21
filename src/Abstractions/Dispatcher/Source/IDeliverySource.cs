using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Source;

/// <summary>A delivery taken from a tier queue plus the handle needed to acknowledge it.</summary>
public sealed record ReceivedDelivery(DeliveryMessage Message, string Receipt);

/// <summary>The <c>deliveries-{tier}</c> queues (SQS in production). Unacknowledged messages are redelivered.</summary>
public interface IDeliverySource
{
    /// <summary>Waits for deliveries (long poll) and returns up to <paramref name="maxCount"/>; empty when none arrived.</summary>
    ValueTask<IReadOnlyList<ReceivedDelivery>> ReceiveAsync(PlanTier tier, int maxCount, CancellationToken cancellationToken);

    ValueTask AcknowledgeAsync(PlanTier tier, ReceivedDelivery delivery, CancellationToken cancellationToken);
}
