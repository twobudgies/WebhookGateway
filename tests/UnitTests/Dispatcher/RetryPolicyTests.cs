using WebhookGateway.Dispatcher.Retry;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class RetryPolicyTests
{
    private static RetryPolicy WithRandom(double value) => new(() => value);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 1800)]
    [InlineData(5, 3600)]
    [InlineData(6, 14400)]
    [InlineData(7, 28800)]
    [InlineData(8, 36000)]
    public void Without_jitter_reduction_the_delay_is_the_schedule_value(int failedAttempt, int expectedSeconds)
    {
        var delay = WithRandom(1.0).NextDelay(failedAttempt, retryAfter: null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Attempts_are_exhausted_after_the_ninth()
    {
        Assert.NotNull(WithRandom(1.0).NextDelay(RetryPolicy.MaxAttempts - 1, null));
        Assert.Null(WithRandom(1.0).NextDelay(RetryPolicy.MaxAttempts, null));
        Assert.Null(WithRandom(1.0).NextDelay(RetryPolicy.MaxAttempts + 5, null));
    }

    [Fact]
    public void Jitter_scales_the_delay_between_one_second_and_the_schedule_value()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), WithRandom(0.5).NextDelay(1, null));
        Assert.Equal(TimeSpan.FromSeconds(1), WithRandom(0.0).NextDelay(1, null));
    }

    [Fact]
    public void Retry_after_raises_the_delay_but_never_lowers_it()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WithRandom(0.5).NextDelay(1, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(30), WithRandom(1.0).NextDelay(1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Total_scheduled_time_is_about_a_day()
    {
        var total = Enumerable.Range(1, RetryPolicy.MaxAttempts - 1)
            .Sum(attempt => WithRandom(1.0).NextDelay(attempt, null)!.Value.TotalHours);

        Assert.InRange(total, 23, 25);
    }

    [Fact]
    public void Attempt_numbers_below_one_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WithRandom(1.0).NextDelay(0, null));
    }
}
