using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Delivery;

/// <summary>Puts the next attempt back on the tier queue with a delay (SQS <c>DelaySeconds</c>, at most 15 minutes).</summary>
public interface IRetryPublisher
{
    ValueTask PublishAsync(PlanTier tier, DeliveryMessage nextAttempt, TimeSpan delay, CancellationToken cancellationToken);
}
