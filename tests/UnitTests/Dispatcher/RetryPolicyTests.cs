using WebhookGateway.Contracts;
using WebhookGateway.Dispatcher.Retry;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class RetryPolicyTests
{
    private static RetryPolicy WithRandom(double value) => new(() => value);

    private static double TotalHours(PlanTier plan) =>
        Enumerable.Range(1, RetryPolicy.MaxAttempts(plan) - 1)
            .Sum(attempt => WithRandom(1.0).NextDelay(plan, attempt, null)!.Value.TotalHours);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 1800)]
    [InlineData(5, 3600)]
    [InlineData(6, 14400)]
    [InlineData(7, 28800)]
    [InlineData(8, 36000)]
    public void The_first_eight_waits_are_the_same_for_every_plan(int failedAttempt, int expectedSeconds)
    {
        foreach (var plan in Enum.GetValues<PlanTier>())
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), WithRandom(1.0).NextDelay(plan, failedAttempt, retryAfter: null));
        }
    }

    [Theory]
    [InlineData(PlanTier.Basic, 9)]
    [InlineData(PlanTier.Advanced, 11)]
    [InlineData(PlanTier.Premium, 15)]
    public void Attempts_by_plan(PlanTier plan, int expected)
    {
        Assert.Equal(expected, RetryPolicy.MaxAttempts(plan));
    }

    [Theory]
    [InlineData(PlanTier.Basic)]
    [InlineData(PlanTier.Advanced)]
    [InlineData(PlanTier.Premium)]
    public void Attempts_are_exhausted_after_the_planned_number(PlanTier plan)
    {
        var max = RetryPolicy.MaxAttempts(plan);

        Assert.NotNull(WithRandom(1.0).NextDelay(plan, max - 1, null));
        Assert.Null(WithRandom(1.0).NextDelay(plan, max, null));
        Assert.Null(WithRandom(1.0).NextDelay(plan, max + 5, null));
    }

    [Fact]
    public void Retry_window_is_about_a_day_for_Basic_three_days_for_Advanced_and_a_week_for_Premium()
    {
        Assert.InRange(TotalHours(PlanTier.Basic), 23, 25);
        Assert.InRange(TotalHours(PlanTier.Advanced), 71, 73);
        Assert.InRange(TotalHours(PlanTier.Premium), 167, 169);
    }

    [Fact]
    public void Daily_waits_stay_between_twelve_and_twenty_four_hours()
    {
        Assert.Equal(TimeSpan.FromHours(12), WithRandom(0.0).NextDelay(PlanTier.Premium, 9, null));
        Assert.Equal(TimeSpan.FromHours(24), WithRandom(1.0).NextDelay(PlanTier.Premium, 9, null));
    }

    [Fact]
    public void Jitter_scales_the_early_waits_between_one_second_and_the_schedule_value()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), WithRandom(0.5).NextDelay(PlanTier.Basic, 1, null));
        Assert.Equal(TimeSpan.FromSeconds(1), WithRandom(0.0).NextDelay(PlanTier.Basic, 1, null));
    }

    [Fact]
    public void Retry_after_raises_the_delay_but_never_lowers_it()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WithRandom(0.5).NextDelay(PlanTier.Basic, 1, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(30), WithRandom(1.0).NextDelay(PlanTier.Basic, 1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Attempt_numbers_below_one_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WithRandom(1.0).NextDelay(PlanTier.Basic, 0, null));
    }
}
