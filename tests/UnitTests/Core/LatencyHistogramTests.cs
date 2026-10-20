using WebhookGateway.Core.Rollups;

namespace WebhookGateway.UnitTests.Core;

public sealed class LatencyHistogramTests
{
    [Fact]
    public void An_empty_histogram_has_no_percentiles()
    {
        Assert.Null(new LatencyHistogram().Percentile(0.5));
    }

    [Fact]
    public void Values_land_in_the_bucket_whose_bound_is_at_or_above_them()
    {
        var histogram = new LatencyHistogram();

        histogram.Add(5);
        histogram.Add(6);
        histogram.Add(30000);
        histogram.Add(90000);

        Assert.Equal(1, histogram.Counts[0]);                       // 5 ms
        Assert.Equal(1, histogram.Counts[1]);                       // 6 ms -> bound 10
        Assert.Equal(1, histogram.Counts[LatencyHistogram.BucketBoundsMs.Count - 1]); // 30000 ms
        Assert.Equal(1, histogram.Counts[^1]);                      // overflow
        Assert.Equal(4, histogram.Total);
        Assert.Equal(5 + 6 + 30000 + 90000, histogram.SumMs);
    }

    [Fact]
    public void Percentiles_report_the_bucket_upper_bound()
    {
        var histogram = new LatencyHistogram();
        for (var i = 0; i < 90; i++)
        {
            histogram.Add(40);   // bucket bound 50
        }

        for (var i = 0; i < 10; i++)
        {
            histogram.Add(900);  // bucket bound 1000
        }

        Assert.Equal(50, histogram.Percentile(0.5));
        Assert.Equal(50, histogram.Percentile(0.9));
        Assert.Equal(1000, histogram.Percentile(0.95));
        Assert.Equal(1000, histogram.Percentile(1.0));
    }

    [Fact]
    public void Overflow_values_report_the_last_bound()
    {
        var histogram = new LatencyHistogram();
        histogram.Add(120000);

        Assert.Equal(30000, histogram.Percentile(0.99));
    }

    [Fact]
    public void Negative_values_count_as_zero()
    {
        var histogram = new LatencyHistogram();
        histogram.Add(-10);

        Assert.Equal(0, histogram.SumMs);
        Assert.Equal(1, histogram.Counts[0]);
    }

    [Fact]
    public void Merging_adds_counts_so_workers_can_write_the_same_row()
    {
        var a = new LatencyHistogram();
        var b = new LatencyHistogram();
        a.Add(40);
        b.Add(40);
        b.Add(900);

        a.Merge(b);

        Assert.Equal(3, a.Total);
        Assert.Equal(980, a.SumMs);
        Assert.Equal(1000, a.Percentile(1.0));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Quantiles_outside_zero_to_one_are_rejected(double quantile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatencyHistogram().Percentile(quantile));
    }
}
