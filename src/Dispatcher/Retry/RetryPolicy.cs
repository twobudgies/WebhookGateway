namespace WebhookGateway.Dispatcher.Retry;

/// <summary>
/// Retry schedule from docs/architecture.md: the first attempt plus 8 retries (9 attempts) over about 24 hours,
/// waiting 30 s, 2 m, 10 m, 30 m, 1 h, 4 h, 8 h, 10 h (full jitter) between them, with the subscriber's
/// <c>Retry-After</c> honored (capped).
/// </summary>
public sealed class RetryPolicy(Func<double> nextRandom)
{
    /// <summary>The first attempt plus one retry per backoff step.</summary>
    public const int MaxAttempts = 9;

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

    private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(1);

    public static RetryPolicy CreateDefault() =>
#pragma warning disable CA5394 // Jitter only spreads load; it is not a security decision.
        new(() => Random.Shared.NextDouble());
#pragma warning restore CA5394

    /// <param name="failedAttempt">The attempt number (1-based) that just failed.</param>
    /// <param name="retryAfter">The subscriber's requested delay, already capped, if any.</param>
    /// <returns>How long to wait before the next attempt, or null when attempts are exhausted.</returns>
    public TimeSpan? NextDelay(int failedAttempt, TimeSpan? retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempt, 1);
        if (failedAttempt >= MaxAttempts)
        {
            return null;
        }

        var ceiling = Backoff[failedAttempt - 1];
        var jittered = TimeSpan.FromTicks((long)(ceiling.Ticks * Math.Clamp(nextRandom(), 0.0, 1.0)));
        var delay = Max(jittered, Min(MinimumDelay, ceiling));
        return retryAfter is { } requested ? Max(delay, requested) : delay;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;
}
