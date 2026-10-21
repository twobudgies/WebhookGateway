namespace WebhookGateway.Aws.DynamoDb;

public sealed class DynamoDbOptions
{
    public string DeliveryStateTable { get; init; } = "delivery_state";

    public string RollupsTable { get; init; } = "rollups";

    /// <summary>How long a delivery_state row lives (TTL). Per-plan retention (30/30/90 days) arrives with the plan lookup.</summary>
    public TimeSpan DeliveryStateRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>1-minute rollup rows live this long (docs/data-plane.md section 7).</summary>
    public TimeSpan RollupMinuteRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Shards of the scheduler index so due deliveries do not pile onto one partition.</summary>
    public int DueBuckets { get; init; } = 16;
}
