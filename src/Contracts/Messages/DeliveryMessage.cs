using System.Text.Json;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Contracts.Messages;

/// <summary>
/// One delivery to send, queues <c>deliveries-{tier}</c> (Fan-out to Dispatcher). The payload is inline up to
/// about 200 KB; larger payloads set <see cref="PayloadRef"/> (S3 archive pointer) and leave
/// <see cref="Payload"/> null. <see cref="OccurredAt"/> is the producer's event time, needed for the outbound
/// webhook body. See docs/data-plane.md section 3.
/// </summary>
public sealed record DeliveryMessage(
    int SchemaVersion,
    TenantId TenantId,
    EventId EventId,
    DeliveryId DeliveryId,
    EndpointId EndpointId,
    string EventType,
    DateTimeOffset OccurredAt,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset NotBefore,
    JsonElement? Payload,
    string? PayloadRef)
{
    public const int CurrentSchemaVersion = 1;
}
