namespace WebhookGateway.Dispatcher.Delivery;

public enum DeliveryAction
{
    /// <summary>Sent and acknowledged by the subscriber.</summary>
    Delivered,

    /// <summary>Failed; the next attempt is back on the queue with a delay.</summary>
    RetryQueued,

    /// <summary>Failed; the next attempt is more than 15 minutes away and waits in the scheduled index.</summary>
    RetryScheduled,

    /// <summary>Will not be retried (permanent failure, missing endpoint or payload).</summary>
    Failed,

    /// <summary>All attempts used.</summary>
    Exhausted,

    /// <summary>Waited longer than the maximum backlog age; not sent.</summary>
    Expired,

    /// <summary>A redelivered message for a delivery that already ended; nothing to do.</summary>
    Skipped,
}
