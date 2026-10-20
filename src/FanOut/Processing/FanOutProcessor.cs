using System.Text;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Routing;

namespace WebhookGateway.FanOut.Processing;

/// <summary>
/// Turns a batch of accepted events into delivery messages: archive first, then one message per matching
/// endpoint, published to the tenant's tier queue. Idempotent: reprocessing an event yields the same delivery ids,
/// so a crash between publish and acknowledge only produces duplicates the Dispatcher already tolerates.
/// </summary>
public sealed class FanOutProcessor(
    ISubscriptionStore subscriptions,
    IEventArchive archive,
    IDeliveryPublisher publisher,
    TimeProvider clock)
{
    /// <summary>Payloads up to this size travel inside the SQS message (docs/data-plane.md section 3).</summary>
    public const int MaxInlinePayloadBytes = 200 * 1024;

    /// <returns>The number of delivery messages published.</returns>
    public async ValueTask<int> ProcessAsync(IReadOnlyList<EventMessage> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return 0;
        }

        var archiveRefs = await archive.WriteBatchAsync(events, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();

        var byTier = new Dictionary<PlanTier, List<DeliveryMessage>>();
        foreach (var e in events)
        {
            var routing = await subscriptions.GetRoutingAsync(e.TenantId, cancellationToken).ConfigureAwait(false);
            if (routing is null)
            {
                continue;
            }

            foreach (var subscription in routing.Subscriptions.Where(s => s.Matches(e.Type)))
            {
                if (!byTier.TryGetValue(routing.Plan, out var list))
                {
                    byTier[routing.Plan] = list = [];
                }

                list.Add(CreateDelivery(e, subscription.EndpointId, archiveRefs, now));
            }
        }

        var published = 0;
        foreach (var (tier, deliveries) in byTier)
        {
            await publisher.PublishAsync(tier, deliveries, cancellationToken).ConfigureAwait(false);
            published += deliveries.Count;
        }

        return published;
    }

    private static DeliveryMessage CreateDelivery(
        EventMessage e,
        EndpointId endpointId,
        IReadOnlyDictionary<EventId, string> archiveRefs,
        DateTimeOffset now)
    {
        var inline = Encoding.UTF8.GetByteCount(e.Payload.GetRawText()) <= MaxInlinePayloadBytes;
        return new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion,
            e.TenantId,
            e.EventId,
            DeliveryId.For(e.EventId, endpointId),
            endpointId,
            e.Type,
            Attempt: 1,
            CreatedAt: now,
            NotBefore: now,
            Payload: inline ? e.Payload : null,
            PayloadRef: inline ? null : archiveRefs[e.EventId]);
    }
}
