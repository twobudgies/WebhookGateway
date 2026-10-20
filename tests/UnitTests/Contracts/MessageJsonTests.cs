using System.Text.Json;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.UnitTests.Contracts;

public sealed class MessageJsonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EventMessage_uses_camelCase_names_and_roundtrips()
    {
        var message = new EventMessage(
            EventMessage.CurrentSchemaVersion,
            EventId.New(),
            TenantId.New(),
            "order.created",
            Now,
            Now.AddMilliseconds(120),
            "key-1",
            JsonDocument.Parse("""{"orderId":42}""").RootElement.Clone());

        var json = ContractJson.Serialize(message);
        var back = ContractJson.Deserialize<EventMessage>(json);

        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"eventId\":\"evt_", json, StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"key-1\"", json, StringComparison.Ordinal);
        Assert.Equal(message.EventId, back.EventId);
        Assert.Equal(message.TenantId, back.TenantId);
        Assert.Equal(42, back.Payload.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public void DeliveryMessage_roundtrips_with_inline_payload()
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var message = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion,
            TenantId.New(),
            eventId,
            DeliveryId.For(eventId, endpointId),
            endpointId,
            "order.created",
            OccurredAt: Now,
            Attempt: 1,
            Now,
            Now,
            JsonDocument.Parse("""{"a":1}""").RootElement.Clone(),
            PayloadRef: null);

        var back = ContractJson.Deserialize<DeliveryMessage>(ContractJson.Serialize(message));

        Assert.Equal(message.DeliveryId, back.DeliveryId);
        Assert.Equal(1, back.Attempt);
        Assert.Equal(Now, back.OccurredAt);
        Assert.Null(back.PayloadRef);
        Assert.Equal(1, back.Payload!.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public void ResultLogLine_writes_the_outcome_as_a_string()
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var line = new ResultLogLine(
            TenantId.New(), endpointId, DeliveryId.For(eventId, endpointId), eventId,
            Attempt: 2, DeliveryOutcome.Retry, StatusCode: 503, QueueWaitMs: 120, ResponseMs: 85, Error: null, Now);

        var json = ContractJson.Serialize(line);

        Assert.Contains("\"outcome\":\"Retry\"", json, StringComparison.Ordinal);
        Assert.Equal(DeliveryOutcome.Retry, ContractJson.Deserialize<ResultLogLine>(json).Outcome);
    }

    [Fact]
    public void Deserialize_rejects_an_identifier_with_the_wrong_prefix()
    {
        const string json = """
            {"schemaVersion":1,"eventId":"ten_01ARZ3NDEKTSV4RRFFQ69G5FAV","tenantId":"ten_01ARZ3NDEKTSV4RRFFQ69G5FAV",
             "type":"x","occurredAt":"2026-10-20T10:00:00Z","receivedAt":"2026-10-20T10:00:00Z",
             "idempotencyKey":null,"payload":{}}
            """;

        Assert.Throws<JsonException>(() => ContractJson.Deserialize<EventMessage>(json));
    }
}
