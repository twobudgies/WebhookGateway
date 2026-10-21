namespace WebhookGateway.Aws;

/// <summary>Where the AWS-backed adapters connect. Production leaves <see cref="ServiceUrl"/> empty and uses the task role.</summary>
public sealed class AwsOptions
{
    public const string SectionName = "Aws";

    /// <summary>Selects the adapters: <c>InMemory</c> (default, no AWS) or <c>Aws</c>.</summary>
    public const string ModeKey = "Backend:Mode";

    public string Region { get; init; } = "ap-south-1";

    /// <summary>Emulator endpoint for local runs, for example <c>http://localhost:5050</c> (Moto). Empty in production.</summary>
    public string? ServiceUrl { get; init; }

    /// <summary>Static credentials for the emulator only; production uses the default credential chain.</summary>
    public string? AccessKey { get; init; }

    public string? SecretKey { get; init; }
}
