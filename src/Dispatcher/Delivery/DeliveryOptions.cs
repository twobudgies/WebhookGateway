namespace WebhookGateway.Dispatcher.Delivery;

public sealed class DeliveryOptions
{
    public const string SectionName = "Dispatcher:Delivery";

    /// <summary>A delivery that was due longer ago than this is not sent; it is marked expired (docs/architecture.md section 6.1).</summary>
    public TimeSpan MaxBacklogAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>SQS can delay a message by at most 15 minutes; longer waits go to the scheduled index.</summary>
    public TimeSpan MaxQueueDelay { get; init; } = TimeSpan.FromMinutes(15);
}
