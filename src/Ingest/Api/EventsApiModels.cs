using System.ComponentModel;
using System.Text.Json;

namespace WebhookGateway.Ingest.Api;

/// <summary>Body of <c>POST /v1/events</c>. These types only describe the API for the OpenAPI document; the endpoint reads the raw body itself.</summary>
public sealed record SendEventRequest(
    [property: Description("What happened, for example order.created. 1 to 128 characters: letters, digits and . _ : -")] string Type,
    [property: Description("Your event data: any JSON object. This is what your customers' endpoints receive under 'data'. At most 256 KB in total.")] JsonElement Data,
    [property: Description("When the event happened (ISO-8601). Optional: defaults to the time it was received.")] DateTimeOffset? OccurredAt = null);

/// <summary>Answer to an accepted event.</summary>
public sealed record EventAccepted(
    [property: Description("Identifier of the stored event, for example evt_01a1... Repeating a request with the same Idempotency-Key returns the same id.")] string EventId);

/// <summary>Error body used by every error response: <c>{ "error": { "code", "message" } }</c>.</summary>
public sealed record ApiErrorResponse(ApiErrorDetail Error);

public sealed record ApiErrorDetail(
    [property: Description("Machine-readable code: invalid_request, unauthorized, tenant_suspended, payload_too_large, rate_limited, quota_exceeded, unavailable")] string Code,
    [property: Description("Human-readable explanation.")] string Message);
