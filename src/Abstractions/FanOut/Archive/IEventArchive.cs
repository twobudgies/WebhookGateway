using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.FanOut.Archive;

/// <summary>
/// Writes accepted events to the payload archive (S3 in production) in one object per batch. The archive is the
/// replay source and holds payloads that are too large to travel inline (docs/data-plane.md section 5).
/// </summary>
public interface IEventArchive
{
    /// <returns>For every event of the batch, a pointer that <c>DeliveryMessage.PayloadRef</c> can carry.</returns>
    ValueTask<IReadOnlyDictionary<EventId, string>> WriteBatchAsync(
        IReadOnlyList<EventMessage> events,
        CancellationToken cancellationToken);
}
