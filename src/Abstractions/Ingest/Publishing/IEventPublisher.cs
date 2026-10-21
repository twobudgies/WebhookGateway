using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Ingest.Publishing;

/// <summary>Durable hand-off of accepted events to Fan-out (SQS queue <c>events</c> in production).</summary>
public interface IEventPublisher
{
    /// <summary>Completes only when the event is durably stored; a failure must surface as an exception (Ingest then returns 5xx).</summary>
    ValueTask PublishAsync(EventMessage message, CancellationToken cancellationToken);
}
