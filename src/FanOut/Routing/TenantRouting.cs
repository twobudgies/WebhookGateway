using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.FanOut.Routing;

/// <summary>One active endpoint and the event type it subscribed to (<c>*</c> matches every type).</summary>
public sealed record EndpointSubscription(EndpointId EndpointId, string EventType)
{
    public const string AnyEventType = "*";

    public bool Matches(string eventType) =>
        string.Equals(EventType, AnyEventType, StringComparison.Ordinal)
        || string.Equals(EventType, eventType, StringComparison.Ordinal);
}

/// <summary>What Fan-out needs to know about a tenant: its plan tier (selects the queue) and active subscriptions.</summary>
public sealed record TenantRouting(PlanTier Plan, IReadOnlyList<EndpointSubscription> Subscriptions);
