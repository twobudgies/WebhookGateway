namespace WebhookGateway.Contracts;

/// <summary>Plan tier; selects the delivery queue set (<c>deliveries-{tier}</c>).</summary>
public enum PlanTier
{
    Basic,
    Advanced,
    Premium,
}

/// <summary>Result of one delivery attempt.</summary>
public enum DeliveryOutcome
{
    Succeeded,
    Retry,
    Failed,
    Exhausted,
}

/// <summary>Status of a delivery that has a row in <c>delivery_state</c> (successes have no row).</summary>
public enum DeliveryStatus
{
    RetryScheduled,
    Parked,
    Scheduled,
    Failed,
    Exhausted,
    Expired,
}
