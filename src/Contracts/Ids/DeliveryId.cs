using System.Globalization;
using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

/// <summary>
/// One delivery of one event to one endpoint. Derived from the event and endpoint ids, so repeating the
/// fan-out of an event produces the same ids (idempotent) and the id is stable across retries. It is the
/// <c>webhook-id</c> header value the subscriber sees. Form: <c>dlv_{eventUuid:N}_{endpointUuid:N}</c>.
/// </summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<DeliveryId>))]
public readonly record struct DeliveryId : IPrefixedId<DeliveryId>
{
    public const string Prefix = "dlv_";

    private const int HexLength = 32;

    private readonly string? _value;

    private DeliveryId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("DeliveryId is not initialized.");

    public EventId EventId => EventId.Parse(EventId.Prefix + ToCanonical(Value.AsSpan(Prefix.Length, HexLength)));

    public EndpointId EndpointId => EndpointId.Parse(EndpointId.Prefix + ToCanonical(Value.AsSpan(Prefix.Length + HexLength + 1, HexLength)));

    public static DeliveryId For(EventId eventId, EndpointId endpointId) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{eventId.Uuid:N}_{endpointId.Uuid:N}"));

    public static DeliveryId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var expectedLength = Prefix.Length + HexLength + 1 + HexLength;
        if (value.Length != expectedLength
            || !value.StartsWith(Prefix, StringComparison.Ordinal)
            || value[Prefix.Length + HexLength] != '_'
            || !Guid.TryParseExact(value.AsSpan(Prefix.Length, HexLength), "N", out var eventUuid)
            || !Guid.TryParseExact(value.AsSpan(Prefix.Length + HexLength + 1, HexLength), "N", out var endpointUuid))
        {
            throw new FormatException($"'{value}' is not a valid delivery identifier.");
        }

        return new DeliveryId(string.Create(CultureInfo.InvariantCulture, $"{Prefix}{eventUuid:N}_{endpointUuid:N}"));
    }

    public override string ToString() => Value;

    private static string ToCanonical(ReadOnlySpan<char> hex) =>
        Guid.ParseExact(hex, "N").ToString("D", CultureInfo.InvariantCulture);
}
