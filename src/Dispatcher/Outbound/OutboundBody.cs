using System.Buffers;
using System.Text.Json;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>The JSON body the subscriber receives: <c>{ id, type, occurredAt, data }</c> (docs/contracts.md section 3).</summary>
public static class OutboundBody
{
    public static byte[] Build(DeliveryMessage message, JsonElement payload)
    {
        ArgumentNullException.ThrowIfNull(message);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("id", message.EventId.Value);
            writer.WriteString("type", message.EventType);
            writer.WriteString("occurredAt", message.OccurredAt);
            writer.WritePropertyName("data");
            payload.WriteTo(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
