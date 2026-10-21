namespace WebhookGateway.Aws.S3;

public sealed class ArchiveOptions
{
    public string Bucket { get; init; } = "webhookgateway-dev";

    /// <summary>Region label in the object key (<c>archive/region=...</c>).</summary>
    public string Region { get; init; } = "ap-south-1";

    /// <summary>KMS key id or alias for server-side encryption; null uses the bucket default.</summary>
    public string? KmsKeyId { get; init; }
}
