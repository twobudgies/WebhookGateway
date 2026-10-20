using System.Security.Cryptography;

namespace WebhookGateway.Contracts.Ids;

/// <summary>
/// ULID helper: 26 Crockford base32 characters, 48-bit millisecond timestamp followed by 80 random bits.
/// Lexicographic order follows creation time (at millisecond resolution).
/// </summary>
public static class Ulid
{
    public const int Length = 26;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New() => New(DateTimeOffset.UtcNow);

    public static string New(DateTimeOffset timestamp)
    {
        var millis = timestamp.ToUnixTimeMilliseconds();
        if (millis < 0 || millis > 0xFFFF_FFFF_FFFFL)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Timestamp does not fit in 48 bits.");
        }

        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);

        UInt128 randomBits = 0;
        foreach (var b in random)
        {
            randomBits = (randomBits << 8) | b;
        }

        var value = ((UInt128)(ulong)millis << 80) | randomBits;

        Span<char> chars = stackalloc char[Length];
        for (var i = Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(chars);
    }

    public static bool IsValid(ReadOnlySpan<char> value)
    {
        if (value.Length != Length || value[0] > '7')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the creation time back out of a valid ULID.</summary>
    public static DateTimeOffset GetTimestamp(string ulid)
    {
        ArgumentNullException.ThrowIfNull(ulid);
        if (!IsValid(ulid))
        {
            throw new ArgumentException("Not a valid ULID.", nameof(ulid));
        }

        UInt128 value = 0;
        foreach (var c in ulid)
        {
            value = (value << 5) | (uint)Alphabet.IndexOf(c, StringComparison.Ordinal);
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)(ulong)(value >> 80));
    }
}
