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

    /// <summary>
    /// Allow endpoints on private networks (RFC 1918, loopback, internal host names) for installs where the operator delivers to their
    /// own services. The cloud metadata address stays blocked. Leave off for a multi-tenant service where customers choose URLs.
    /// </summary>
    public bool AllowPrivateNetworks { get; init; }
}
