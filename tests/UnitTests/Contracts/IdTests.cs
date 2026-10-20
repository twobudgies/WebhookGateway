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
    [InlineData("ten_0192f3a4-7c1b-7d2e-9a10-3b5c8e7f1a22")] // wrong prefix for EventId
    [InlineData("evt_not-a-uuid")]
    [InlineData("evt_0192f3a47c1b7d2e9a103b5c8e7f1a22")]     // must be the hyphenated form
    public void Parse_rejects_invalid_event_ids(string value)
    {
        Assert.Throws<FormatException>(() => EventId.Parse(value));
    }

    [Fact]
    public void Parse_accepts_uppercase_and_returns_the_canonical_lowercase_form()
    {
        var id = EventId.Parse("evt_0192F3A4-7C1B-7D2E-9A10-3B5C8E7F1A22");

        Assert.Equal("evt_0192f3a4-7c1b-7d2e-9a10-3b5c8e7f1a22", id.Value);
    }

    [Fact]
    public void Ids_embed_a_standard_guid()
    {
        var id = TenantId.New();

        Assert.Equal(7, id.Uuid.Version);
        Assert.Equal(TenantId.Prefix + id.Uuid.ToString("D"), id.Value);
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
    [InlineData("dlv_0192f3a47c1b7d2e9a103b5c8e7f1a22")]
    [InlineData("dlv_0192f3a47c1b7d2e9a103b5c8e7f1a22-0192f3a47c1b7d2e9a103b5c8e7f1a22")]
    [InlineData("xxx_0192f3a47c1b7d2e9a103b5c8e7f1a22_0192f3a47c1b7d2e9a103b5c8e7f1a22")]
    public void DeliveryId_rejects_invalid_values(string value)
    {
        Assert.Throws<FormatException>(() => DeliveryId.Parse(value));
    }
}
