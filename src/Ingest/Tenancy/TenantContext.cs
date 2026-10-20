using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Tenancy;

public enum TenantStatus
{
    Active,
    Suspended,
    Closed,
}

/// <summary>Limits that apply to one tenant at accept time (plan values plus overrides).</summary>
/// <param name="EventsPerSecond">Sustained accept rate.</param>
/// <param name="BurstCapacity">Token bucket capacity (events that may arrive at once).</param>
/// <param name="MaxPayloadBytes">Largest accepted request body.</param>
public sealed record PlanLimits(int EventsPerSecond, int BurstCapacity, int MaxPayloadBytes)
{
    public const int DefaultMaxPayloadBytes = 256 * 1024;

    /// <summary>Provisional plan values from docs/tenancy-and-limits.md (only the rates are owner numbers).</summary>
    public static PlanLimits For(PlanTier plan) => plan switch
    {
        PlanTier.Basic => new PlanLimits(100, 200, DefaultMaxPayloadBytes),
        PlanTier.Advanced => new PlanLimits(500, 1000, DefaultMaxPayloadBytes),
        PlanTier.Premium => new PlanLimits(1000, 2000, DefaultMaxPayloadBytes),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };
}

public sealed record TenantContext(TenantId TenantId, PlanTier Plan, TenantStatus Status, PlanLimits Limits);
