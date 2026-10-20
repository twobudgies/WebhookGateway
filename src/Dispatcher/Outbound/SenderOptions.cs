namespace WebhookGateway.Dispatcher.Outbound;

public sealed class SenderOptions
{
    public const string SectionName = "Dispatcher:Sender";

    /// <summary>Whole-request timeout (docs/architecture.md: 10 seconds).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Plain http is for local development and tests only.</summary>
    public bool AllowInsecureHttp { get; init; }

    public int MaxConnectionsPerServer { get; init; } = 100;
}
