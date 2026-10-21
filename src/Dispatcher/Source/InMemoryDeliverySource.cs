using System.Collections.Concurrent;
using System.Globalization;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Source;

/// <summary>Source for tests and a no-AWS local run. Redelivery is not simulated.</summary>
public sealed class InMemoryDeliverySource : IDeliverySource
{
    private readonly ConcurrentDictionary<PlanTier, ConcurrentQueue<DeliveryMessage>> _pending = new();
    private readonly ConcurrentQueue<string> _acknowledged = new();
    private long _receiptCounter;

    public IReadOnlyCollection<string> Acknowledged => _acknowledged;

    public void Add(PlanTier tier, DeliveryMessage message) =>
        _pending.GetOrAdd(tier, _ => new ConcurrentQueue<DeliveryMessage>()).Enqueue(message);

    public async ValueTask<IReadOnlyList<ReceivedDelivery>> ReceiveAsync(PlanTier tier, int maxCount, CancellationToken cancellationToken)
    {
        var batch = new List<ReceivedDelivery>();
        var queue = _pending.GetOrAdd(tier, _ => new ConcurrentQueue<DeliveryMessage>());
        while (batch.Count < maxCount && queue.TryDequeue(out var message))
        {
            batch.Add(new ReceivedDelivery(message, Interlocked.Increment(ref _receiptCounter).ToString(CultureInfo.InvariantCulture)));
        }

        if (batch.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        return batch;
    }

    public ValueTask AcknowledgeAsync(PlanTier tier, ReceivedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _acknowledged.Enqueue(delivery.Receipt);
        return ValueTask.CompletedTask;
    }
}
