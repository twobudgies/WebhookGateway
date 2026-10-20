using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Contracts.Messages;

/// <summary>One line of the result log written by Dispatcher (one per attempt). No payloads, no secrets.</summary>
public sealed record ResultLogLine(
    TenantId TenantId,
    EndpointId EndpointId,
    DeliveryId DeliveryId,
    EventId EventId,
    int Attempt,
    DeliveryOutcome Outcome,
    int? StatusCode,
    long QueueWaitMs,
    long ResponseMs,
    string? Error,
    DateTimeOffset Ts);
