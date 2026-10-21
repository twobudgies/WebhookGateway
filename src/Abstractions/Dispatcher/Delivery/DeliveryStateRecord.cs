using System.Text.Json;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Dispatcher.Delivery;

/// <summary>
/// The row kept for a delivery that did not succeed at once (docs/data-plane.md section 4, <c>delivery_state</c>).
/// Successful deliveries leave no row.
/// </summary>
public sealed record DeliveryStateRecord(
    TenantId TenantId,
    DeliveryId DeliveryId,
    EndpointId EndpointId,
    EventId EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DeliveryStatus Status,
    int Attempt,
    DateTimeOffset? NextAttemptAt,
    int? LastStatusCode,
    string? LastError,
    JsonElement? Payload,
    string? PayloadRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    PlanTier Tier)
{
    /// <summary>No further attempts will be made.</summary>
    public bool IsFinal => Status is DeliveryStatus.Failed or DeliveryStatus.Exhausted or DeliveryStatus.Expired;
}
