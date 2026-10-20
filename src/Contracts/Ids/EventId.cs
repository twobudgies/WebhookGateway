using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

[JsonConverter(typeof(PrefixedIdJsonConverter<EventId>))]
public readonly record struct EventId : IPrefixedId<EventId>
{
    public const string Prefix = "evt_";

    private readonly string? _value;

    private EventId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("EventId is not initialized.");

    public static EventId New() => new(PrefixedIdFormat.Create(Prefix));

    public static EventId Parse(string value) => new(PrefixedIdFormat.Validate(Prefix, value));

    /// <summary>The ULID part without the prefix.</summary>
    public string Ulid => Value[Prefix.Length..];

    public override string ToString() => Value;
}
