namespace WebhookGateway.Contracts.Ids;

/// <summary>UUID version 7 (RFC 9562): a standard GUID whose first 48 bits are the creation time in Unix milliseconds.</summary>
public static class Uuid7
{
    public static Guid New() => Guid.CreateVersion7();

    public static Guid New(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);

    /// <summary>Reads the creation time back out of a version 7 UUID.</summary>
    public static DateTimeOffset GetTimestamp(Guid uuid)
    {
        if (uuid.Version != 7)
        {
            throw new ArgumentException("Not a version 7 UUID.", nameof(uuid));
        }

        Span<byte> bytes = stackalloc byte[16];
        uuid.TryWriteBytes(bytes, bigEndian: true, out _);
        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
