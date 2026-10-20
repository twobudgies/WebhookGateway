using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.UnitTests.Contracts;

public sealed class IdTests
{
    [Fact]
    public void New_ids_carry_their_prefix()
    {
        Assert.StartsWith("ten_", TenantId.New().Value, StringComparison.Ordinal);
        Assert.StartsWith("evt_", EventId.New().Value, StringComparison.Ordinal);
        Assert.StartsWith("ep_", EndpointId.New().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_roundtrips_a_generated_id()
    {
        var id = EventId.New();

        Assert.Equal(id, EventId.Parse(id.Value));
    }

    [Theory]
    [InlineData("evt_")]
    [InlineData("ten_01ARZ3NDEKTSV4RRFFQ69G5FAV")] // wrong prefix for EventId
    [InlineData("evt_notaulid")]
    public void Parse_rejects_invalid_event_ids(string value)
    {
        Assert.Throws<FormatException>(() => EventId.Parse(value));
    }

    [Fact]
    public void Default_id_is_not_usable()
    {
        EventId id = default;

        Assert.Throws<InvalidOperationException>(() => id.Value);
    }

    [Fact]
    public void DeliveryId_is_deterministic_for_the_same_event_and_endpoint()
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();

        Assert.Equal(DeliveryId.For(eventId, endpointId), DeliveryId.For(eventId, endpointId));
    }

    [Fact]
    public void DeliveryId_differs_per_endpoint()
    {
        var eventId = EventId.New();

        Assert.NotEqual(DeliveryId.For(eventId, EndpointId.New()), DeliveryId.For(eventId, EndpointId.New()));
    }

    [Fact]
    public void DeliveryId_exposes_its_event_and_endpoint()
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();

        var delivery = DeliveryId.For(eventId, endpointId);

        Assert.Equal(eventId, delivery.EventId);
        Assert.Equal(endpointId, delivery.EndpointId);
        Assert.Equal(delivery, DeliveryId.Parse(delivery.Value));
    }

    [Theory]
    [InlineData("dlv_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("dlv_01ARZ3NDEKTSV4RRFFQ69G5FAV-01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("xxx_01ARZ3NDEKTSV4RRFFQ69G5FAV_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void DeliveryId_rejects_invalid_values(string value)
    {
        Assert.Throws<FormatException>(() => DeliveryId.Parse(value));
    }
}
