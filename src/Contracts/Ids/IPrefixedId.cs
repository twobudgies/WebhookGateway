using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

/// <summary>An identifier that is serialized as a plain string such as <c>evt_01J...</c>.</summary>
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
    public static string Create(string prefix) => prefix + Ulid.New();

    public static string Validate(string prefix, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Ulid.IsValid(value.AsSpan(prefix.Length)))
        {
            throw new FormatException($"'{value}' is not a valid identifier with prefix '{prefix}'.");
        }

        return value;
    }
}
