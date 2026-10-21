using System.Collections.Concurrent;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Source;

/// <summary>Source for tests and a no-AWS local run. Redelivery is not simulated.</summary>
public sealed class InMemoryEventSource : IEventSource
{
    private readonly ConcurrentQueue<EventMessage> _pending = new();
    private readonly ConcurrentQueue<string> _acknowledged = new();
    private long _receiptCounter;

    public IReadOnlyCollection<string> Acknowledged => _acknowledged;

    public void Add(EventMessage message) => _pending.Enqueue(message);

    public async ValueTask<IReadOnlyList<ReceivedEvent>> ReceiveAsync(int maxCount, CancellationToken cancellationToken)
    {
        var batch = new List<ReceivedEvent>();
        while (batch.Count < maxCount && _pending.TryDequeue(out var message))
        {
            batch.Add(new ReceivedEvent(message, Interlocked.Increment(ref _receiptCounter).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (batch.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        return batch;
    }

    public ValueTask AcknowledgeAsync(IReadOnlyList<ReceivedEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            _acknowledged.Enqueue(e.Receipt);
        }

        return ValueTask.CompletedTask;
    }
}
