using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.UnitTests.Contracts;

public sealed class UlidTests
{
    [Fact]
    public void New_produces_valid_26_character_value()
    {
        var ulid = Ulid.New();

        Assert.Equal(Ulid.Length, ulid.Length);
        Assert.True(Ulid.IsValid(ulid));
    }

    [Fact]
    public void New_values_sort_by_timestamp()
    {
        var earlier = Ulid.New(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        var later = Ulid.New(new DateTimeOffset(2026, 10, 20, 10, 0, 1, TimeSpan.Zero));

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void GetTimestamp_returns_the_creation_time()
    {
        var created = new DateTimeOffset(2026, 10, 20, 10, 30, 15, 123, TimeSpan.Zero);

        var ulid = Ulid.New(created);

        Assert.Equal(created, Ulid.GetTimestamp(ulid));
    }

    [Fact]
    public void New_does_not_repeat_within_the_same_millisecond()
    {
        var stamp = DateTimeOffset.UtcNow;

        var values = Enumerable.Range(0, 1000).Select(_ => Ulid.New(stamp)).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(1000, values.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAI")] // contains I, not in the alphabet
    [InlineData("81ARZ3NDEKTSV4RRFFQ69G5FAV")] // first character overflows 128 bits
    public void IsValid_rejects_malformed_values(string value)
    {
        Assert.False(Ulid.IsValid(value));
    }
}
