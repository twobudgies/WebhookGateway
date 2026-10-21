using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Source;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class DeliveryConsumerTests
{
    private readonly InMemoryDeliverySource _source = new();

    private DeliveryConsumer Consumer(IDeliveryProcessor processor, CapturingLogger<DeliveryConsumer>? log = null) =>
        new(_source, processor, Options.Create(new DeliveryConsumerOptions()), log ?? (ILogger<DeliveryConsumer>)NullLogger<DeliveryConsumer>.Instance);

    private DeliveryMessage Add(PlanTier tier)
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var now = DateTimeOffset.UtcNow;
        var message = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, TenantId.New(), eventId, DeliveryId.For(eventId, endpointId), endpointId,
            "order.created", now, 1, now, now, JsonDocument.Parse("{}").RootElement.Clone(), null);
        _source.Add(tier, message);
        return message;
    }

    [Fact]
    public async Task A_processed_delivery_is_acknowledged_with_its_tier()
    {
        var processor = new RecordingProcessor();
        var message = Add(PlanTier.Premium);

        var count = await Consumer(processor).RunOnceAsync(PlanTier.Premium, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal((message.DeliveryId, PlanTier.Premium), Assert.Single(processor.Seen));
        Assert.Single(_source.Acknowledged);
    }

    [Fact]
    public async Task A_failing_delivery_is_logged_and_not_acknowledged_while_others_continue()
    {
        var log = new CapturingLogger<DeliveryConsumer>();
        var processor = new RecordingProcessor();
        var bad = Add(PlanTier.Basic);
        Add(PlanTier.Basic);
        processor.FailFor = bad.DeliveryId;

        await Consumer(processor, log).RunOnceAsync(PlanTier.Basic, CancellationToken.None);

        Assert.Equal(2, processor.Seen.Count);
        Assert.Single(_source.Acknowledged);
        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(bad.DeliveryId.Value, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tiers_are_read_from_their_own_queue()
    {
        Add(PlanTier.Advanced);

        var count = await Consumer(new RecordingProcessor()).RunOnceAsync(PlanTier.Basic, CancellationToken.None);

        Assert.Equal(0, count);
    }

    private sealed class RecordingProcessor : IDeliveryProcessor
    {
        private readonly List<(DeliveryId, PlanTier)> _seen = [];

        public List<(DeliveryId, PlanTier)> Seen => _seen;

        public DeliveryId? FailFor { get; set; }

        public ValueTask<DeliveryAction> ProcessAsync(DeliveryMessage message, PlanTier tier, CancellationToken cancellationToken)
        {
            lock (_seen)
            {
                _seen.Add((message.DeliveryId, tier));
            }

            return message.DeliveryId == FailFor
                ? throw new InvalidOperationException("storage down")
                : ValueTask.FromResult(DeliveryAction.Delivered);
        }
    }
}
