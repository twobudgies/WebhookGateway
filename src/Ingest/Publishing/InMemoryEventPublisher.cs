using System.Collections.Concurrent;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Ingest.Publishing;

/// <summary>Publisher for tests and a no-AWS local run. Not durable.</summary>
public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<EventMessage> _messages = new();

    public IReadOnlyCollection<EventMessage> Messages => _messages;

    public ValueTask PublishAsync(EventMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }
}
