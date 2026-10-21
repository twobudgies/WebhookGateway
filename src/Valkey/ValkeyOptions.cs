namespace WebhookGateway.Valkey;

public sealed class ValkeyOptions
{
    public const string SectionName = "Valkey";

    /// <summary>StackExchange.Redis connection string, for example <c>localhost:6379</c>. Empty means Valkey is not used.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Per-command timeout. Short on purpose: the accept path falls back to local limits instead of waiting.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMilliseconds(150);
}
