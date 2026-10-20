using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

/// <summary>An identifier serialized as a plain string: a type prefix plus a UUIDv7, such as <c>evt_0192f3a4-7c1b-7d2e-9a10-3b5c8e7f1a22</c>.</summary>
public interface IPrefixedId<TSelf>
    where TSelf : struct, IPrefixedId<TSelf>
{
    string Value { get; }

    static abstract TSelf Parse(string value);
}

/// <summary>Writes and reads <see cref="IPrefixedId{TSelf}"/> values as JSON strings, validating on read.</summary>
public sealed class PrefixedIdJsonConverter<T> : JsonConverter<T>
    where T : struct, IPrefixedId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Identifier must be a string.");
        try
        {
            return T.Parse(text);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

internal static class PrefixedIdFormat
{
    public static string Create(string prefix) => prefix + Uuid7.New().ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Checks prefix and UUID and returns the canonical (lowercase, hyphenated) form.</summary>
    public static string Validate(string prefix, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(value.AsSpan(prefix.Length), "D", out var uuid))
        {
            throw new FormatException($"'{value}' is not a valid identifier with prefix '{prefix}'.");
        }

        return prefix + uuid.ToString("D", CultureInfo.InvariantCulture);
    }
}
