using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Source;

/// <summary>An event taken from the queue plus the handle needed to acknowledge it.</summary>
public sealed record ReceivedEvent(EventMessage Message, string Receipt);

/// <summary>The <c>events</c> queue (SQS in production). Unacknowledged events are redelivered.</summary>
public interface IEventSource
{
    /// <summary>Waits for events (long poll) and returns up to <paramref name="maxCount"/>; empty when none arrived.</summary>
    ValueTask<IReadOnlyList<ReceivedEvent>> ReceiveAsync(int maxCount, CancellationToken cancellationToken);

    ValueTask AcknowledgeAsync(IReadOnlyList<ReceivedEvent> events, CancellationToken cancellationToken);
}
