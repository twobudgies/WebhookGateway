using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.FanOut.Delivery;

namespace WebhookGateway.Aws.Sqs;

/// <summary>Fan-out sends first attempts, Dispatcher sends retries; both use the tier queue and SQS delay for pacing.</summary>
public sealed class SqsDeliveryPublisher(SqsQueues queues) : IDeliveryPublisher, IRetryPublisher
{
    /// <summary>SQS allows at most 15 minutes of delay.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    public ValueTask PublishAsync(PlanTier tier, IReadOnlyList<DeliveryMessage> deliveries, CancellationToken cancellationToken) =>
        SqsSender.SendAsync(
            queues,
            SqsQueueNames.Deliveries(tier),
            [.. deliveries.Select(d => (ContractJson.Serialize(d), 0))],
            cancellationToken);

    public ValueTask PublishAsync(PlanTier tier, DeliveryMessage nextAttempt, TimeSpan delay, CancellationToken cancellationToken)
    {
        var seconds = (int)Math.Clamp(Math.Ceiling(delay.TotalSeconds), 0, MaxDelay.TotalSeconds);
        return SqsSender.SendAsync(
            queues,
            SqsQueueNames.Deliveries(tier),
            [(ContractJson.Serialize(nextAttempt), seconds)],
            cancellationToken);
    }
}
