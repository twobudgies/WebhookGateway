using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Source;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Source;
using WebhookGateway.Ingest.Publishing;

namespace WebhookGateway.Postgres.Backend;

/// <summary>Queue names are the same as with SQS (docs/data-plane.md).</summary>
public static class QueueNames
{
    public const string Events = "events";

    public static string Deliveries(PlanTier tier) => $"deliveries-{tier.ToString().ToLowerInvariant()}";
}

public sealed class PostgresEventPublisher(PostgresMessageStore queue) : IEventPublisher
{
    public ValueTask PublishAsync(EventMessage message, CancellationToken cancellationToken) =>
        queue.SendAsync(QueueNames.Events, [(ContractJson.Serialize(message), TimeSpan.Zero)], cancellationToken);
}

/// <summary>First attempts from Fan-out and retries from the Dispatcher, with the delay as queue visibility.</summary>
public sealed class PostgresDeliveryPublisher(PostgresMessageStore queue) : IDeliveryPublisher, IRetryPublisher
{
    public ValueTask PublishAsync(PlanTier tier, IReadOnlyList<DeliveryMessage> deliveries, CancellationToken cancellationToken) =>
        queue.SendAsync(QueueNames.Deliveries(tier), [.. deliveries.Select(d => (ContractJson.Serialize(d), TimeSpan.Zero))], cancellationToken);

    public ValueTask PublishAsync(PlanTier tier, DeliveryMessage nextAttempt, TimeSpan delay, CancellationToken cancellationToken) =>
        queue.SendAsync(QueueNames.Deliveries(tier), [(ContractJson.Serialize(nextAttempt), delay < TimeSpan.Zero ? TimeSpan.Zero : delay)], cancellationToken);
}

public sealed partial class PostgresEventSource(PostgresMessageStore queue, ILogger<PostgresEventSource> logger, TimeSpan? wait = null) : IEventSource
{
    private static readonly TimeSpan Visibility = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, QueueMessage> _leases = [];

    public async ValueTask<IReadOnlyList<ReceivedEvent>> ReceiveAsync(int maxCount, CancellationToken cancellationToken)
    {
        var messages = await queue.ReceiveAsync(QueueNames.Events, Math.Clamp(maxCount, 1, 10), Visibility, wait ?? TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var events = new List<ReceivedEvent>();
        foreach (var m in messages)
        {
            try
            {
                var receipt = $"{m.Id}:{m.Receipt}";
                events.Add(new ReceivedEvent(ContractJson.Deserialize<EventMessage>(m.Body), receipt));
                lock (_leases)
                {
                    _leases[receipt] = m;
                }
            }
            catch (JsonException ex)
            {
                // Left leased: it reappears, and after five receives lands in the dead-letter queue. The body is never logged.
                LogUnreadable(logger, ex, m.Id);
            }
        }

        return events;
    }

    public async ValueTask AcknowledgeAsync(IReadOnlyList<ReceivedEvent> events, CancellationToken cancellationToken)
    {
        foreach (var e in events)
        {
            QueueMessage? lease;
            lock (_leases)
            {
                _leases.Remove(e.Receipt, out lease);
            }

            if (lease is not null)
            {
                await queue.DeleteAsync(lease, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable message {MessageId} left on the events queue for redelivery and the dead-letter queue")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, long messageId);
}

public sealed partial class PostgresDeliverySource(PostgresMessageStore queue, ILogger<PostgresDeliverySource> logger, TimeSpan? wait = null) : IDeliverySource
{
    private static readonly TimeSpan Visibility = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, QueueMessage> _leases = [];

    public async ValueTask<IReadOnlyList<ReceivedDelivery>> ReceiveAsync(PlanTier tier, int maxCount, CancellationToken cancellationToken)
    {
        var messages = await queue.ReceiveAsync(QueueNames.Deliveries(tier), Math.Clamp(maxCount, 1, 10), Visibility, wait ?? TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var deliveries = new List<ReceivedDelivery>();
        foreach (var m in messages)
        {
            try
            {
                var receipt = $"{m.Id}:{m.Receipt}";
                deliveries.Add(new ReceivedDelivery(ContractJson.Deserialize<DeliveryMessage>(m.Body), receipt));
                lock (_leases)
                {
                    _leases[receipt] = m;
                }
            }
            catch (JsonException ex)
            {
                LogUnreadable(logger, ex, m.Id, tier);
            }
        }

        return deliveries;
    }

    public async ValueTask AcknowledgeAsync(PlanTier tier, ReceivedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        QueueMessage? lease;
        lock (_leases)
        {
            _leases.Remove(delivery.Receipt, out lease);
        }

        if (lease is not null)
        {
            await queue.DeleteAsync(lease, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable message {MessageId} left on the {Tier} delivery queue for redelivery and the dead-letter queue")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, long messageId, PlanTier tier);
}
