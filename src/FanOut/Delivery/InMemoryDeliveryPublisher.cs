using System.Collections.Concurrent;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Delivery;

public sealed class InMemoryDeliveryPublisher : IDeliveryPublisher
{
    private readonly ConcurrentQueue<(PlanTier Tier, DeliveryMessage Message)> _published = new();

    public IReadOnlyCollection<(PlanTier Tier, DeliveryMessage Message)> Published => _published;

    public ValueTask PublishAsync(PlanTier tier, IReadOnlyList<DeliveryMessage> deliveries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        foreach (var d in deliveries)
        {
            _published.Enqueue((tier, d));
        }

        return ValueTask.CompletedTask;
    }
}
