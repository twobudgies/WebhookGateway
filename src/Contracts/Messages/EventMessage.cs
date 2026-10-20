using System.Text.Json;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Contracts.Messages;

/// <summary>Accepted event, queue <c>events</c> (Ingest to Fan-out). See docs/data-plane.md section 3.</summary>
public sealed record EventMessage(
    int SchemaVersion,
    EventId EventId,
    TenantId TenantId,
    string Type,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? IdempotencyKey,
    JsonElement Payload)
{
    public const int CurrentSchemaVersion = 1;
}
