using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

[JsonConverter(typeof(PrefixedIdJsonConverter<EndpointId>))]
public readonly record struct EndpointId : IPrefixedId<EndpointId>
{
    public const string Prefix = "ep_";

    private readonly string? _value;

    private EndpointId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("EndpointId is not initialized.");

    public static EndpointId New() => new(PrefixedIdFormat.Create(Prefix));

    public static EndpointId Parse(string value) => new(PrefixedIdFormat.Validate(Prefix, value));

    /// <summary>The UUID part as a <see cref="System.Guid"/>.</summary>
    public Guid Uuid => Guid.ParseExact(Value.AsSpan(Prefix.Length), "D");

    public override string ToString() => Value;
}
