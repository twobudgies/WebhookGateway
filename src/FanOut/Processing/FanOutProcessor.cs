using System.Text;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Core.Rollups;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Routing;

namespace WebhookGateway.FanOut.Processing;

/// <summary>
/// Turns a batch of accepted events into delivery messages: archive first, then one message per matching
/// endpoint, published to the tenant's tier queue. Idempotent: reprocessing an event yields the same delivery ids,
/// so a crash between publish and acknowledge only produces duplicates the Dispatcher already tolerates.
/// </summary>
public sealed partial class FanOutProcessor(
    ISubscriptionStore subscriptions,
    IEventArchive archive,
    IDeliveryPublisher publisher,
    TimeProvider clock,
    ILogger<FanOutProcessor> logger,
    RollupAccumulator rollups)
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

        IReadOnlyDictionary<EventId, string> archiveRefs;
        try
        {
            archiveRefs = await archive.WriteBatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogArchiveFailed(logger, events.Count, events[0].EventId.Value);
            throw;
        }

        var now = clock.GetUtcNow();

        var byTier = new Dictionary<PlanTier, List<DeliveryMessage>>();
        var unmatched = 0;
        foreach (var e in events)
        {
            var routing = await subscriptions.GetRoutingAsync(e.TenantId, cancellationToken).ConfigureAwait(false);
            if (routing is null)
            {
                LogNoRouting(logger, e.TenantId.Value, e.EventId.Value);
                unmatched++;
                continue;
            }

            var matched = false;
            foreach (var subscription in routing.Subscriptions.Where(s => s.Matches(e.Type)))
            {
                matched = true;
                if (!byTier.TryGetValue(routing.Plan, out var list))
                {
                    byTier[routing.Plan] = list = [];
                }

                list.Add(CreateDelivery(e, subscription.EndpointId, archiveRefs, now));
                rollups.RecordCreated(e.TenantId, subscription.EndpointId, now);
            }

            if (!matched)
            {
                unmatched++;
            }
        }

        var published = 0;
        foreach (var (tier, deliveries) in byTier)
        {
            try
            {
                await publisher.PublishAsync(tier, deliveries, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPublishFailed(logger, tier, deliveries.Count);
                throw;
            }

            published += deliveries.Count;
        }

        LogBatchProcessed(logger, events.Count, published, unmatched);
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
            e.OccurredAt,
            Attempt: 1,
            CreatedAt: now,
            NotBefore: now,
            Payload: inline ? e.Payload : null,
            PayloadRef: inline ? null : archiveRefs[e.EventId]);
    }

    // The exception itself is logged once by the caller (EventConsumer); these lines add the context it lacks.
    [LoggerMessage(Level = LogLevel.Error, Message = "Archive write failed for a batch of {EventCount} events starting at {FirstEventId}")]
    private static partial void LogArchiveFailed(ILogger logger, int eventCount, string firstEventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing {DeliveryCount} deliveries to tier {Tier} failed")]
    private static partial void LogPublishFailed(ILogger logger, PlanTier tier, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No routing for tenant {TenantId}; event {EventId} produces no deliveries")]
    private static partial void LogNoRouting(ILogger logger, string tenantId, string eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fan-out batch: {EventCount} events, {DeliveryCount} deliveries, {UnmatchedCount} events without a matching endpoint")]
    private static partial void LogBatchProcessed(ILogger logger, int eventCount, int deliveryCount, int unmatchedCount);
}
