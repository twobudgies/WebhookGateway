using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Core.Rollups;
using WebhookGateway.FanOut;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Processing;
using WebhookGateway.FanOut.Routing;
using WebhookGateway.FanOut.Source;

namespace WebhookGateway.UnitTests.FanOut;

public sealed class EventConsumerTests
{
    private readonly InMemorySubscriptionStore _subscriptions = new();
    private readonly InMemoryDeliveryPublisher _publisher = new();
    private readonly InMemoryEventSource _source = new();

    private EventConsumer CreateConsumer(IDeliveryPublisher? publisher = null) =>
        new(
            _source,
            new FanOutProcessor(_subscriptions, new InMemoryEventArchive(), publisher ?? _publisher, new FakeTimeProvider(), NullLogger<FanOutProcessor>.Instance, new RollupAccumulator()),
            NullLogger<EventConsumer>.Instance);

    private EventMessage AddEvent(TenantId tenant)
    {
        var message = new EventMessage(
            EventMessage.CurrentSchemaVersion, EventId.New(), tenant, "order.created",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, JsonDocument.Parse("{}").RootElement.Clone());
        _source.Add(message);
        return message;
    }

    [Fact]
    public async Task Events_are_acknowledged_after_their_deliveries_are_published()
    {
        var tenant = TenantId.New();
        _subscriptions.Set(tenant, new TenantRouting(PlanTier.Basic, [new EndpointSubscription(EndpointId.New(), "*")]));
        AddEvent(tenant);
        AddEvent(tenant);

        var published = await CreateConsumer().RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, published);
        Assert.Equal(2, _source.Acknowledged.Count);
    }

    [Fact]
    public async Task A_publish_failure_leaves_events_unacknowledged_for_redelivery()
    {
        var tenant = TenantId.New();
        _subscriptions.Set(tenant, new TenantRouting(PlanTier.Basic, [new EndpointSubscription(EndpointId.New(), "*")]));
        AddEvent(tenant);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateConsumer(new FailingPublisher()).RunOnceAsync(CancellationToken.None));

        Assert.Empty(_source.Acknowledged);
    }

    [Fact]
    public async Task An_empty_queue_does_nothing()
    {
        Assert.Equal(0, await CreateConsumer().RunOnceAsync(CancellationToken.None));
    }

    private sealed class FailingPublisher : IDeliveryPublisher
    {
        public ValueTask PublishAsync(PlanTier tier, IReadOnlyList<DeliveryMessage> deliveries, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("queue unavailable");
    }
}
