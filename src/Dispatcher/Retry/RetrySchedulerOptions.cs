namespace WebhookGateway.Dispatcher.Retry;

public sealed class RetrySchedulerOptions
{
    public const string SectionName = "Dispatcher:Scheduler";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Most deliveries moved back to a queue per poll.</summary>
    public int BatchSize { get; init; } = 200;
}
