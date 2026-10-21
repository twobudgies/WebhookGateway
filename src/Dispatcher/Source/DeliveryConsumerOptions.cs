namespace WebhookGateway.Dispatcher.Source;

public sealed class DeliveryConsumerOptions
{
    public const string SectionName = "Dispatcher:Consumer";

    /// <summary>Deliveries in flight at once per tier queue. Sending is async I/O, so this is limited by connections, not threads.</summary>
    public int MaxConcurrencyPerTier { get; init; } = 100;

    public int BatchSize { get; init; } = 10;

    public TimeSpan FailureBackoff { get; init; } = TimeSpan.FromSeconds(5);
}
