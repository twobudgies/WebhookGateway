using WebhookGateway.Contracts;

namespace WebhookGateway.FanOut.Delivery;

/// <summary>Queue names from docs/data-plane.md: one delivery queue set per plan tier.</summary>
public static class QueueNames
{
    public const string Events = "events";

    public static string DeliveriesFor(PlanTier plan) => plan switch
    {
        PlanTier.Basic => "deliveries-basic",
        PlanTier.Advanced => "deliveries-advanced",
        PlanTier.Premium => "deliveries-premium",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };
}
