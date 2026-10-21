using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Postgres;

/// <summary>The database stores the UUID; the application adds the type prefix (docs/database.md, D30).</summary>
internal static class ConfigIds
{
    public static TenantId Tenant(Guid id) => TenantId.Parse($"ten_{id}");

    public static EndpointId Endpoint(Guid id) => EndpointId.Parse($"ep_{id}");

    public static Guid Uuid(TenantId id) => Guid.Parse(id.Value["ten_".Length..]);

    public static Guid Uuid(EndpointId id) => Guid.Parse(id.Value["ep_".Length..]);
}
