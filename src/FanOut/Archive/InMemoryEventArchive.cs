using System.Collections.Concurrent;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Archive;

public sealed class InMemoryEventArchive : IEventArchive
{
    private readonly ConcurrentDictionary<EventId, EventMessage> _stored = new();

    public IReadOnlyDictionary<EventId, EventMessage> Stored => _stored;

    public ValueTask<IReadOnlyDictionary<EventId, string>> WriteBatchAsync(
        IReadOnlyList<EventMessage> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        var refs = new Dictionary<EventId, string>();
        foreach (var e in events)
        {
            _stored[e.EventId] = e;
            refs[e.EventId] = $"memory://archive/{e.EventId}";
        }

        return ValueTask.FromResult<IReadOnlyDictionary<EventId, string>>(refs);
    }
}
