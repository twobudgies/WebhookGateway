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

    /// <summary>The ULID part without the prefix.</summary>
    public string Ulid => Value[Prefix.Length..];

    public override string ToString() => Value;
}
