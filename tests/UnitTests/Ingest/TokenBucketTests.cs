using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Ingest.Limits;

namespace WebhookGateway.UnitTests.Ingest;

public sealed class TokenBucketTests
{
    private static (bool Taken, int Remaining, TimeSpan RetryAfter) Take(TokenBucket bucket, FakeTimeProvider time) =>
        bucket.TryTake(time.GetTimestamp(), time.TimestampFrequency);

    [Fact]
    public void Allows_up_to_capacity_then_rejects()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 3, ratePerSecond: 1, time.GetTimestamp());

        Assert.True(Take(bucket, time).Taken);
        Assert.True(Take(bucket, time).Taken);
        var third = Take(bucket, time);
        var fourth = Take(bucket, time);

        Assert.True(third.Taken);
        Assert.Equal(0, third.Remaining);
        Assert.False(fourth.Taken);
    }

    [Fact]
    public void Reports_how_long_until_a_token_is_available()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 1, ratePerSecond: 4, time.GetTimestamp());
        Take(bucket, time);

        var rejected = Take(bucket, time);

        Assert.False(rejected.Taken);
        Assert.Equal(TimeSpan.FromMilliseconds(250), rejected.RetryAfter);
    }

    [Fact]
    public void Refills_over_time()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 10, ratePerSecond: 10, time.GetTimestamp());
        for (var i = 0; i < 10; i++)
        {
            Take(bucket, time);
        }

        time.Advance(TimeSpan.FromMilliseconds(500));

        var taken = 0;
        while (Take(bucket, time).Taken)
        {
            taken++;
        }

        Assert.Equal(5, taken);
    }

    [Fact]
    public void Never_holds_more_than_capacity()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 2, ratePerSecond: 100, time.GetTimestamp());

        time.Advance(TimeSpan.FromMinutes(10));

        Assert.True(Take(bucket, time).Taken);
        Assert.True(Take(bucket, time).Taken);
        Assert.False(Take(bucket, time).Taken);
    }

    [Fact]
    public void Rejects_non_positive_configuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(1, 0, 0));
    }
}
