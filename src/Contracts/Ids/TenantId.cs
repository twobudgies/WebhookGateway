using System.Text.Json.Serialization;

namespace WebhookGateway.Contracts.Ids;

[JsonConverter(typeof(PrefixedIdJsonConverter<TenantId>))]
public readonly record struct TenantId : IPrefixedId<TenantId>
{
    public const string Prefix = "ten_";

    private readonly string? _value;

    private TenantId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("TenantId is not initialized.");

    public static TenantId New() => new(PrefixedIdFormat.Create(Prefix));

    public static TenantId Parse(string value) => new(PrefixedIdFormat.Validate(Prefix, value));

    /// <summary>The UUID part as a <see cref="System.Guid"/>.</summary>
    public Guid Uuid => Guid.ParseExact(Value.AsSpan(Prefix.Length), "D");

    public override string ToString() => Value;
}
