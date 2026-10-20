using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.UnitTests.Contracts;

public sealed class Uuid7Tests
{
    [Fact]
    public void New_produces_a_version_7_uuid()
    {
        Assert.Equal(7, Uuid7.New().Version);
    }

    [Fact]
    public void GetTimestamp_returns_the_creation_time()
    {
        var created = new DateTimeOffset(2026, 10, 20, 10, 30, 15, 123, TimeSpan.Zero);

        var uuid = Uuid7.New(created);

        Assert.Equal(created, Uuid7.GetTimestamp(uuid));
    }

    [Fact]
    public void Values_created_later_sort_after_earlier_ones()
    {
        var earlier = Uuid7.New(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        var later = Uuid7.New(new DateTimeOffset(2026, 10, 20, 10, 0, 1, TimeSpan.Zero));

        Assert.True(string.CompareOrdinal(earlier.ToString("D"), later.ToString("D")) < 0);
    }

    [Fact]
    public void Values_do_not_repeat_within_the_same_millisecond()
    {
        var stamp = DateTimeOffset.UtcNow;

        var values = Enumerable.Range(0, 1000).Select(_ => Uuid7.New(stamp)).ToHashSet();

        Assert.Equal(1000, values.Count);
    }

    [Fact]
    public void GetTimestamp_rejects_other_uuid_versions()
    {
        Assert.Throws<ArgumentException>(() => Uuid7.GetTimestamp(Guid.NewGuid()));
    }
}
