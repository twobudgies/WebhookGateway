using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

/// <summary>
/// One delivery of one event to one endpoint. Derived from the event and endpoint ids, so repeating the
/// fan-out of an event produces the same ids (idempotent) and the id is stable across retries. It is the
/// <c>webhook-id</c> header value the subscriber sees.
/// </summary>
[JsonConverter(typeof(PrefixedIdJsonConverter<DeliveryId>))]
public readonly record struct DeliveryId : IPrefixedId<DeliveryId>
{
    public const string Prefix = "dlv_";

    private readonly string? _value;

    private DeliveryId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("DeliveryId is not initialized.");

    public EventId EventId => EventId.Parse(string.Concat(EventId.Prefix, Value.AsSpan(Prefix.Length, Ids.Ulid.Length)));

    public EndpointId EndpointId => EndpointId.Parse(string.Concat(EndpointId.Prefix, Value.AsSpan(Prefix.Length + Ids.Ulid.Length + 1)));

    public static DeliveryId For(EventId eventId, EndpointId endpointId) =>
        new($"{Prefix}{eventId.Ulid}_{endpointId.Ulid}");

    public static DeliveryId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var expectedLength = Prefix.Length + Ids.Ulid.Length + 1 + Ids.Ulid.Length;
        if (value.Length != expectedLength
            || !value.StartsWith(Prefix, StringComparison.Ordinal)
            || value[Prefix.Length + Ids.Ulid.Length] != '_'
            || !Ids.Ulid.IsValid(value.AsSpan(Prefix.Length, Ids.Ulid.Length))
            || !Ids.Ulid.IsValid(value.AsSpan(Prefix.Length + Ids.Ulid.Length + 1)))
        {
            throw new FormatException($"'{value}' is not a valid delivery identifier.");
        }

        return new DeliveryId(value);
    }

    public override string ToString() => Value;
}
