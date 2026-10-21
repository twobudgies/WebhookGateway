using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Postgres;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>, PostgreSQL on 5432); skipped when it is not reachable.</summary>
public sealed class PostgresConfigTests
{
    private const string Connection = "Host=localhost;Port=5432;Database=webhook;Username=webhook;Password=webhook;Timeout=2";

    private static async Task<NpgsqlDataSource?> ConnectAsync()
    {
        var dataSource = NpgsqlDataSource.Create(Connection);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await new MigrationRunner(dataSource, NullLogger<MigrationRunner>.Instance).RunAsync(MigrationsDirectory(), CancellationToken.None);
            return dataSource;
        }
        catch (NpgsqlException)
        {
            await dataSource.DisposeAsync();
            return null;
        }
    }

    private static string MigrationsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "db", "migrations")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir!, "db", "migrations");
    }

    private static async Task Exec(NpgsqlDataSource db, string sql, params (string, object)[] parameters)
    {
        await using var command = db.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static IOptions<PostgresOptions> Options() => Microsoft.Extensions.Options.Options.Create(new PostgresOptions());

    [Fact]
    public async Task Migrations_are_idempotent_and_seed_the_three_plans()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var again = await new MigrationRunner(db, NullLogger<MigrationRunner>.Instance).RunAsync(MigrationsDirectory(), CancellationToken.None);

        Assert.Empty(again);
        await using var command = db.CreateCommand("SELECT count(*) FROM webhook.plans WHERE code IN ('basic','advanced','premium')");
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task An_api_key_resolves_to_the_tenant_with_plan_limits_and_overrides()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var tenant = Guid.CreateVersion7();
        var apiKey = $"wgk_test_{Guid.NewGuid():N}";
        await Exec(db, "INSERT INTO webhook.tenants (id, name, plan_code) VALUES (@id, 'T', 'advanced')", ("id", tenant));
        await Exec(db, "INSERT INTO webhook.api_keys (id, tenant_id, key_hash, prefix) VALUES (@id, @t, @h, 'wgk_test')", ("id", Guid.CreateVersion7()), ("t", tenant), ("h", ApiKeyHasher.Hash(apiKey)));
        await Exec(db, "INSERT INTO webhook.tenant_overrides (tenant_id, field, value) VALUES (@t, 'accept_rate', '750')", ("t", tenant));
        try
        {
            var store = new PostgresApiKeyStore(db, new FakeTimeProvider(), Options(), NullLogger<PostgresApiKeyStore>.Instance);

            var context = await store.FindByKeyHashAsync(ApiKeyHasher.Hash(apiKey), CancellationToken.None);

            Assert.NotNull(context);
            Assert.Equal(PlanTier.Advanced, context.Plan);
            Assert.Equal(TenantStatus.Active, context.Status);
            Assert.Equal(750, context.Limits.EventsPerSecond);
            Assert.Equal(1000, context.Limits.BurstCapacity);
            Assert.Equal(TimeSpan.FromHours(24), context.Limits.IdempotencyWindow);
            Assert.Equal(tenant.ToString(), context.TenantId.Value["ten_".Length..]);
        }
        finally
        {
            await Exec(db, "DELETE FROM webhook.tenants WHERE id = @id", ("id", tenant));
        }
    }

    [Fact]
    public async Task Revoked_and_unknown_keys_resolve_to_nothing()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var tenant = Guid.CreateVersion7();
        var apiKey = $"wgk_test_{Guid.NewGuid():N}";
        await Exec(db, "INSERT INTO webhook.tenants (id, name, plan_code) VALUES (@id, 'T', 'basic')", ("id", tenant));
        await Exec(db, "INSERT INTO webhook.api_keys (id, tenant_id, key_hash, prefix, revoked_at) VALUES (@id, @t, @h, 'wgk_test', now())", ("id", Guid.CreateVersion7()), ("t", tenant), ("h", ApiKeyHasher.Hash(apiKey)));
        try
        {
            var store = new PostgresApiKeyStore(db, new FakeTimeProvider(), Options(), NullLogger<PostgresApiKeyStore>.Instance);

            Assert.Null(await store.FindByKeyHashAsync(ApiKeyHasher.Hash(apiKey), CancellationToken.None));
            Assert.Null(await store.FindByKeyHashAsync(ApiKeyHasher.Hash("wgk_nope"), CancellationToken.None));
        }
        finally
        {
            await Exec(db, "DELETE FROM webhook.tenants WHERE id = @id", ("id", tenant));
        }
    }

    [Fact]
    public async Task Routing_lists_only_subscriptions_of_active_endpoints_on_the_plans_queue_tier()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var tenant = Guid.CreateVersion7();
        var active = Guid.CreateVersion7();
        var disabled = Guid.CreateVersion7();
        await Exec(db, "INSERT INTO webhook.tenants (id, name, plan_code) VALUES (@id, 'T', 'premium')", ("id", tenant));
        foreach (var (id, status) in new[] { (active, "active"), (disabled, "disabled") })
        {
            await Exec(db, "INSERT INTO webhook.endpoints (id, tenant_id, url, status, secret_ciphertext, secret_kms_key_id) VALUES (@id, @t, 'https://example.com/h', @s, '\\x00', 'k')", ("id", id), ("t", tenant), ("s", status));
            await Exec(db, "INSERT INTO webhook.subscriptions (id, tenant_id, endpoint_id, event_type) VALUES (@id, @t, @e, '*')", ("id", Guid.CreateVersion7()), ("t", tenant), ("e", id));
        }

        try
        {
            var store = new PostgresSubscriptionStore(db, new FakeTimeProvider(), Options());

            var routing = await store.GetRoutingAsync(Contracts.Ids.TenantId.Parse($"ten_{tenant}"), CancellationToken.None);

            Assert.NotNull(routing);
            Assert.Equal(PlanTier.Premium, routing.Plan);
            var subscription = Assert.Single(routing.Subscriptions);
            Assert.Equal($"ep_{active}", subscription.EndpointId.Value);
        }
        finally
        {
            await Exec(db, "DELETE FROM webhook.tenants WHERE id = @id", ("id", tenant));
        }
    }
}
