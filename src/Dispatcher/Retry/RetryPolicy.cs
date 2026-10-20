using WebhookGateway.Contracts;

namespace WebhookGateway.Dispatcher.Retry;

/// <summary>
/// Retry schedule, by plan (docs/architecture.md section 6, decision D31):
/// <list type="bullet">
/// <item>Every plan starts with the same eight waits: 30 s, 2 m, 10 m, 30 m, 1 h, 4 h, 8 h, 10 h (about 24 hours),
/// each with full jitter.</item>
/// <item>Advanced then waits two more days (about 3 days in total), Premium six more (about 7 days in total).</item>
/// <item>Those daily waits use "equal jitter" (between half and all of 24 h) so the window stays close to 3 or 7 days.</item>
/// <item>The subscriber's <c>Retry-After</c> is honored (already capped by the sender).</item>
/// </list>
/// An attempt number is 1-based; the first attempt is the original send.
/// </summary>
public sealed class RetryPolicy(Func<double> nextRandom)
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(10),
    ];

    private static readonly TimeSpan DailyWait = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(1);

    public static RetryPolicy CreateDefault() =>
#pragma warning disable CA5394 // Jitter only spreads load; it is not a security decision.
        new(() => Random.Shared.NextDouble());
#pragma warning restore CA5394

    /// <summary>The first attempt plus one retry per wait for the plan.</summary>
    public static int MaxAttempts(PlanTier plan) => 1 + Backoff.Length + DailyRetries(plan);

    /// <param name="plan">The tenant's plan tier.</param>
    /// <param name="failedAttempt">The attempt number (1-based) that just failed.</param>
    /// <param name="retryAfter">The subscriber's requested delay, already capped, if any.</param>
    /// <returns>How long to wait before the next attempt, or null when attempts are exhausted.</returns>
    public TimeSpan? NextDelay(PlanTier plan, int failedAttempt, TimeSpan? retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempt, 1);
        if (failedAttempt >= MaxAttempts(plan))
        {
            return null;
        }

        var random = Math.Clamp(nextRandom(), 0.0, 1.0);
        TimeSpan delay;
        if (failedAttempt <= Backoff.Length)
        {
            var ceiling = Backoff[failedAttempt - 1];
            var jittered = TimeSpan.FromTicks((long)(ceiling.Ticks * random));
            delay = Max(jittered, Min(MinimumDelay, ceiling));
        }
        else
        {
            delay = TimeSpan.FromTicks((long)(DailyWait.Ticks * (0.5 + (0.5 * random))));
        }

        return retryAfter is { } requested ? Max(delay, requested) : delay;
    }

    private static int DailyRetries(PlanTier plan) => plan switch
    {
        PlanTier.Basic => 0,
        PlanTier.Advanced => 2,
        PlanTier.Premium => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;
}
