namespace WebhookGateway.Core.Rollups;

/// <summary>
/// Fixed-bucket latency histogram. Buckets are additive, so many workers can write the same rollup row with
/// atomic increments and percentiles are still computable (docs/data-plane.md section 4). Percentiles report the
/// upper bound of the bucket that contains the rank, i.e. they never understate.
/// </summary>
public sealed class LatencyHistogram
{
    /// <summary>Upper bounds in milliseconds; the final bucket collects everything above the last bound.</summary>
    public static readonly IReadOnlyList<long> BucketBoundsMs = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000];

    private readonly long[] _counts = new long[BucketBoundsMs.Count + 1];

    public long Total { get; private set; }

    public long SumMs { get; private set; }

    public IReadOnlyList<long> Counts => _counts;

    public void Add(long milliseconds)
    {
        var ms = Math.Max(0, milliseconds);
        var index = 0;
        while (index < BucketBoundsMs.Count && ms > BucketBoundsMs[index])
        {
            index++;
        }

        _counts[index]++;
        Total++;
        SumMs += ms;
    }

    public void Merge(LatencyHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (var i = 0; i < _counts.Length; i++)
        {
            _counts[i] += other._counts[i];
        }

        Total += other.Total;
        SumMs += other.SumMs;
    }

    /// <param name="quantile">0 to 1, for example 0.95.</param>
    /// <returns>Upper bound in ms of the bucket holding that quantile, or null when empty. The overflow bucket reports the last bound.</returns>
    public long? Percentile(double quantile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantile, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantile, 1.0);
        if (Total == 0)
        {
            return null;
        }

        var rank = Math.Max(1, (long)Math.Ceiling(quantile * Total));
        long seen = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            seen += _counts[i];
            if (seen >= rank)
            {
                return BucketBoundsMs[Math.Min(i, BucketBoundsMs.Count - 1)];
            }
        }

        return BucketBoundsMs[^1];
    }
}
