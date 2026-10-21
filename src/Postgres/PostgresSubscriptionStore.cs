using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.FanOut.Routing;

namespace WebhookGateway.Postgres;

/// <summary>Plan tier and the subscriptions of active endpoints for a tenant, cached. Unknown or inactive tenants resolve to null.</summary>
public sealed class PostgresSubscriptionStore : ISubscriptionStore
{
    private readonly NpgsqlDataSource _db;
    private readonly TtlCache<Contracts.Ids.TenantId, TenantRouting> _cache;

    public PostgresSubscriptionStore(NpgsqlDataSource db, TimeProvider clock, IOptions<PostgresOptions> options)
    {
        _db = db;
        _cache = new TtlCache<Contracts.Ids.TenantId, TenantRouting>(clock, options.Value.CacheTtl, options.Value.NegativeCacheTtl);
    }

    public ValueTask<TenantRouting?> GetRoutingAsync(Contracts.Ids.TenantId tenantId, CancellationToken cancellationToken) =>
        _cache.GetOrLoadAsync(tenantId, LoadAsync, cancellationToken);

    private async ValueTask<TenantRouting?> LoadAsync(Contracts.Ids.TenantId tenantId, CancellationToken cancellationToken)
    {
        var id = ConfigIds.Uuid(tenantId);
        await using var connection = await _db.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        PlanTier plan;
        await using (var tenant = new NpgsqlCommand(
            "SELECT p.queue_tier FROM webhook.tenants t JOIN webhook.plans p ON p.code = t.plan_code WHERE t.id = @id AND t.status = 'active'",
            connection))
        {
            tenant.Parameters.AddWithValue("id", id);
            var tier = await tenant.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (tier is null)
            {
                return null;
            }

            plan = Enum.Parse<PlanTier>(tier, ignoreCase: true);
        }

        var subscriptions = new List<EndpointSubscription>();
        await using var command = new NpgsqlCommand(
            "SELECT s.endpoint_id, s.event_type FROM webhook.subscriptions s JOIN webhook.endpoints e ON e.id = s.endpoint_id WHERE s.tenant_id = @id AND e.status = 'active'",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            subscriptions.Add(new EndpointSubscription(ConfigIds.Endpoint(reader.GetGuid(0)), reader.GetString(1)));
        }

        return new TenantRouting(plan, subscriptions);
    }
}
