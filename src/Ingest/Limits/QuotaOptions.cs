namespace WebhookGateway.Ingest.Limits;

public sealed class QuotaOptions
{
    public const string SectionName = "Ingest:Quota";

    /// <summary>
    /// Reject with <c>429 quota_exceeded</c> above the plan's monthly quota. Off by default: events are always counted
    /// (billing), but the plan quotas are placeholders until the owner sets them (D42).
    /// </summary>
    public bool Enforce { get; init; }
}
