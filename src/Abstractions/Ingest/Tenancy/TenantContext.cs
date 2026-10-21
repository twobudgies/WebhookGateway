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
/// <param name="IdempotencyWindow">How long an <c>Idempotency-Key</c> is remembered.</param>
/// <param name="MonthlyQuota">Events accepted per calendar month (UTC).</param>
public sealed record PlanLimits(int EventsPerSecond, int BurstCapacity, int MaxPayloadBytes, TimeSpan IdempotencyWindow, long MonthlyQuota)
{
    public const int DefaultMaxPayloadBytes = 256 * 1024;

    /// <summary>Provisional plan values from docs/tenancy-and-limits.md (only the rates are owner numbers; the monthly quotas are placeholders).</summary>
    public static PlanLimits For(PlanTier plan) => plan switch
    {
        PlanTier.Basic => new PlanLimits(100, 200, DefaultMaxPayloadBytes, TimeSpan.FromHours(1), 1_000_000),
        PlanTier.Advanced => new PlanLimits(500, 1000, DefaultMaxPayloadBytes, TimeSpan.FromHours(24), 20_000_000),
        PlanTier.Premium => new PlanLimits(1000, 2000, DefaultMaxPayloadBytes, TimeSpan.FromHours(24), 100_000_000),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };
}

public sealed record TenantContext(TenantId TenantId, PlanTier Plan, TenantStatus Status, PlanLimits Limits);
