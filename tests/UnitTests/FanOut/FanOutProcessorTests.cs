using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Processing;
using WebhookGateway.FanOut.Routing;

namespace WebhookGateway.UnitTests.FanOut;

public sealed class FanOutProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemorySubscriptionStore _subscriptions = new();
    private readonly InMemoryEventArchive _archive = new();
    private readonly InMemoryDeliveryPublisher _publisher = new();
    private readonly FanOutProcessor _processor;

    public FanOutProcessorTests() =>
        _processor = new FanOutProcessor(_subscriptions, _archive, _publisher, new FakeTimeProvider(Now));

    private static EventMessage Event(TenantId tenant, string type = "order.created", string payloadJson = """{"id":1}""") =>
        new(
            EventMessage.CurrentSchemaVersion,
            EventId.New(),
            tenant,
            type,
            Now,
            Now,
            IdempotencyKey: null,
            JsonDocument.Parse(payloadJson).RootElement.Clone());

    private TenantId Tenant(PlanTier plan, params (EndpointId Endpoint, string Type)[] subscriptions)
    {
        var tenant = TenantId.New();
        _subscriptions.Set(
            tenant,
            new TenantRouting(plan, [.. subscriptions.Select(s => new EndpointSubscription(s.Endpoint, s.Type))]));
        return tenant;
    }

    [Fact]
    public async Task Creates_one_delivery_per_matching_endpoint()
    {
        var a = EndpointId.New();
        var b = EndpointId.New();
        var other = EndpointId.New();
        var tenant = Tenant(PlanTier.Basic, (a, "order.created"), (b, "*"), (other, "invoice.paid"));
        var e = Event(tenant);

        var count = await _processor.ProcessAsync([e], CancellationToken.None);

        Assert.Equal(2, count);
        var endpoints = _publisher.Published.Select(p => p.Message.EndpointId).ToHashSet();
        Assert.Equal(2, endpoints.Count);
        Assert.Contains(a, endpoints);
        Assert.Contains(b, endpoints);
        Assert.DoesNotContain(other, endpoints);
    }

    [Fact]
    public async Task Delivery_ids_are_deterministic_so_reprocessing_is_idempotent()
    {
        var endpoint = EndpointId.New();
        var tenant = Tenant(PlanTier.Basic, (endpoint, "order.created"));
        var e = Event(tenant);

        await _processor.ProcessAsync([e], CancellationToken.None);
        await _processor.ProcessAsync([e], CancellationToken.None);

        var ids = _publisher.Published.Select(p => p.Message.DeliveryId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(DeliveryId.For(e.EventId, endpoint), ids[0]);
    }

    [Theory]
    [InlineData(PlanTier.Basic)]
    [InlineData(PlanTier.Advanced)]
    [InlineData(PlanTier.Premium)]
    public async Task Deliveries_go_to_the_tenants_tier(PlanTier plan)
    {
        var tenant = Tenant(plan, (EndpointId.New(), "*"));

        await _processor.ProcessAsync([Event(tenant)], CancellationToken.None);

        Assert.All(_publisher.Published, p => Assert.Equal(plan, p.Tier));
    }

    [Fact]
    public async Task Delivery_carries_inline_payload_and_first_attempt_fields()
    {
        var tenant = Tenant(PlanTier.Basic, (EndpointId.New(), "*"));
        var e = Event(tenant, payloadJson: """{"orderId":42}""");

        await _processor.ProcessAsync([e], CancellationToken.None);

        var message = Assert.Single(_publisher.Published).Message;
        Assert.Equal(1, message.Attempt);
        Assert.Equal(Now, message.CreatedAt);
        Assert.Equal(Now, message.NotBefore);
        Assert.Equal("order.created", message.EventType);
        Assert.Null(message.PayloadRef);
        Assert.Equal(42, message.Payload!.Value.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Large_payload_is_sent_by_reference_not_inline()
    {
        var tenant = Tenant(PlanTier.Basic, (EndpointId.New(), "*"));
        var big = new string('x', FanOutProcessor.MaxInlinePayloadBytes + 10);
        var e = Event(tenant, payloadJson: $"{{\"blob\":\"{big}\"}}");

        await _processor.ProcessAsync([e], CancellationToken.None);

        var message = Assert.Single(_publisher.Published).Message;
        Assert.Null(message.Payload);
        Assert.NotNull(message.PayloadRef);
    }

    [Fact]
    public async Task Events_are_archived_even_when_nobody_subscribes()
    {
        var tenant = Tenant(PlanTier.Basic);
        var e = Event(tenant);

        var count = await _processor.ProcessAsync([e], CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(_publisher.Published);
        Assert.Contains(e.EventId, _archive.Stored.Keys);
    }

    [Fact]
    public async Task Unknown_tenant_produces_no_deliveries()
    {
        var count = await _processor.ProcessAsync([Event(TenantId.New())], CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_failing_archive_prevents_any_publish()
    {
        var tenant = Tenant(PlanTier.Basic, (EndpointId.New(), "*"));
        var processor = new FanOutProcessor(_subscriptions, new ThrowingArchive(), _publisher, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await processor.ProcessAsync([Event(tenant)], CancellationToken.None));

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Empty_batch_does_nothing()
    {
        Assert.Equal(0, await _processor.ProcessAsync([], CancellationToken.None));
    }

    private sealed class ThrowingArchive : IEventArchive
    {
        public ValueTask<IReadOnlyDictionary<EventId, string>> WriteBatchAsync(
            IReadOnlyList<EventMessage> events,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("archive unavailable");
    }
}
