using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.ControlPlane;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Postgres;

namespace WebhookGateway.IntegrationTests.ControlPlane;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>, PostgreSQL on 5432); skipped when it is not reachable.</summary>
public sealed class AdminApiTests
{
    private const string Connection = "Host=localhost;Port=5432;Database=webhook;Username=webhook;Password=webhook;Timeout=2";
    private static readonly string[] AnyType = ["*"];
    private static readonly string[] TwoTypes = ["order.created", "invoice.paid"];

    private sealed class Env : IAsyncDisposable
    {
        public const string AdminKey = "test-admin-key";
        public static readonly string LocalKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        private readonly WebApplicationFactory<ControlPlaneApiMarker> _factory;
        private readonly List<string> _tenants = [];

        private Env(NpgsqlDataSource db, WebApplicationFactory<ControlPlaneApiMarker> factory)
        {
            Db = db;
            _factory = factory;
        }

        public NpgsqlDataSource Db { get; }

        public static async Task<Env?> StartAsync()
        {
            var db = NpgsqlDataSource.Create(Connection);
            try
            {
                await using var connection = await db.OpenConnectionAsync();
                var dir = AppContext.BaseDirectory;
                while (dir is not null && !Directory.Exists(Path.Combine(dir, "db", "migrations")))
                {
                    dir = Path.GetDirectoryName(dir);
                }

                await new MigrationRunner(db, NullLogger<MigrationRunner>.Instance).RunAsync(Path.Combine(dir!, "db", "migrations"), CancellationToken.None);
            }
            catch (NpgsqlException)
            {
                await db.DisposeAsync();
                return null;
            }

            var factory = new WebApplicationFactory<ControlPlaneApiMarker>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Postgres:ConnectionString", Connection);
                builder.UseSetting("ControlPlane:AdminKey", AdminKey);
                builder.UseSetting("Secrets:LocalKey", LocalKey);
            });
            return new Env(db, factory);
        }

        public HttpClient Client(string? key = AdminKey)
        {
            var client = _factory.CreateClient();
            if (key is not null)
            {
                client.DefaultRequestHeaders.Add("X-Admin-Key", key);
            }

            return client;
        }

        public async Task<string> CreateTenantAsync(HttpClient client, string plan = "basic")
        {
            using var response = await client.PostAsJsonAsync(new Uri("/admin/v1/tenants", UriKind.Relative), new { name = "Acme", plan });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
            _tenants.Add(id);
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var id in _tenants)
            {
                await using var command = Db.CreateCommand("DELETE FROM webhook.tenants WHERE id = @id");
                command.Parameters.AddWithValue("id", Guid.Parse(id));
                await command.ExecuteNonQueryAsync();
            }

            await Db.DisposeAsync();
            _factory.Dispose();
        }
    }

    [Fact]
    public async Task The_admin_api_requires_the_admin_key()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var anonymous = env.Client(key: null);
        using var wrong = env.Client(key: "nope");

        using var a = await anonymous.PostAsJsonAsync(new Uri("/admin/v1/tenants", UriKind.Relative), new { name = "x", plan = "basic" });
        using var b = await wrong.GetAsync(new Uri($"/admin/v1/tenants/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, b.StatusCode);
    }

    [Fact]
    public async Task A_tenant_key_and_endpoint_created_through_the_api_are_usable_by_the_services()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client();
        var tenantId = await env.CreateTenantAsync(client);

        using var keyResponse = await client.PostAsync(new Uri($"/admin/v1/tenants/{tenantId}/api-keys", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Created, keyResponse.StatusCode);
        var apiKey = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;

        using var endpointResponse = await client.PostAsJsonAsync(
            new Uri($"/admin/v1/tenants/{tenantId}/endpoints", UriKind.Relative),
            new { url = "https://example.com/hook", eventTypes = TwoTypes });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        var created = await endpointResponse.Content.ReadFromJsonAsync<JsonElement>();
        var endpointId = created.GetProperty("id").GetString()!;
        var secret = created.GetProperty("secret").GetString()!;

        var options = Options.Create(new PostgresOptions());
        var tenant = await new PostgresApiKeyStore(env.Db, new FakeTimeProvider(), options, NullLogger<PostgresApiKeyStore>.Instance)
            .FindByKeyHashAsync(ApiKeyHasher.Hash(apiKey), CancellationToken.None);
        var routing = await new PostgresSubscriptionStore(env.Db, new FakeTimeProvider(), options)
            .GetRoutingAsync(TenantId.Parse($"ten_{tenantId}"), CancellationToken.None);
        var target = await new PostgresEndpointStore(env.Db, LocalSecretProtector.FromBase64(Env.LocalKey), new FakeTimeProvider(), options, NullLogger<PostgresEndpointStore>.Instance)
            .GetAsync(TenantId.Parse($"ten_{tenantId}"), EndpointId.Parse($"ep_{endpointId}"), CancellationToken.None);

        Assert.Equal($"ten_{tenantId}", tenant!.TenantId.Value);
        Assert.Equal(2, routing!.Subscriptions.Count);
        Assert.Equal(secret, Assert.Single(target!.Secrets).ToText());
    }

    [Fact]
    public async Task A_revoked_key_stops_resolving()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client();
        var tenantId = await env.CreateTenantAsync(client);
        using var keyResponse = await client.PostAsync(new Uri($"/admin/v1/tenants/{tenantId}/api-keys", UriKind.Relative), content: null);
        var key = await keyResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var revoke = await client.DeleteAsync(new Uri($"/admin/v1/tenants/{tenantId}/api-keys/{key.GetProperty("id").GetString()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var store = new PostgresApiKeyStore(env.Db, new FakeTimeProvider(), Options.Create(new PostgresOptions()), NullLogger<PostgresApiKeyStore>.Instance);
        Assert.Null(await store.FindByKeyHashAsync(ApiKeyHasher.Hash(key.GetProperty("key").GetString()!), CancellationToken.None));
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("not a url")]
    public async Task An_endpoint_url_that_is_not_allowed_is_rejected(string url)
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client();
        var tenantId = await env.CreateTenantAsync(client);

        using var response = await client.PostAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}/endpoints", UriKind.Relative), new { url, eventTypes = AnyType });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_plan_endpoint_cap_is_enforced_and_a_downgrade_below_the_count_is_refused()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client();
        var tenantId = await env.CreateTenantAsync(client, plan: "premium");
        for (var i = 0; i < 11; i++)
        {
            using var ok = await client.PostAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}/endpoints", UriKind.Relative), new { url = $"https://example.com/{i}", eventTypes = AnyType });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var downgrade = await client.PatchAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}", UriKind.Relative), new { plan = "basic" });

        Assert.Equal(HttpStatusCode.Conflict, downgrade.StatusCode);
    }

    [Fact]
    public async Task Suspending_a_tenant_and_disabling_an_endpoint_take_effect_in_the_stores()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client();
        var tenantId = await env.CreateTenantAsync(client);
        using var endpointResponse = await client.PostAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}/endpoints", UriKind.Relative), new { url = "https://example.com/hook", eventTypes = AnyType });
        var endpointId = (await endpointResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var options = Options.Create(new PostgresOptions());
        var tenant = TenantId.Parse($"ten_{tenantId}");

        using var disable = await client.PatchAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}/endpoints/{endpointId}", UriKind.Relative), new { status = "disabled" });
        var routingAfterDisable = await new PostgresSubscriptionStore(env.Db, new FakeTimeProvider(), options).GetRoutingAsync(tenant, CancellationToken.None);
        using var suspend = await client.PatchAsJsonAsync(new Uri($"/admin/v1/tenants/{tenantId}", UriKind.Relative), new { status = "suspended" });
        var routingAfterSuspend = await new PostgresSubscriptionStore(env.Db, new FakeTimeProvider(), options).GetRoutingAsync(tenant, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Empty(routingAfterDisable!.Subscriptions);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Null(routingAfterSuspend);
    }

    [Fact]
    public async Task The_openapi_document_lists_every_admin_call_and_the_admin_key_scheme()
    {
        await using var env = await Env.StartAsync();
        if (env is null)
        {
            return;
        }

        using var client = env.Client(key: null);

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/admin/v1/tenants", "/admin/v1/tenants/{tenantId}", "/admin/v1/tenants/{tenantId}/api-keys",
            "/admin/v1/tenants/{tenantId}/api-keys/{keyId}", "/admin/v1/tenants/{tenantId}/endpoints",
            "/admin/v1/tenants/{tenantId}/endpoints/{endpointId}", "/admin/v1/tenants/{tenantId}/endpoints/{endpointId}/subscriptions",
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"missing {path}");
        }

        var scheme = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("X-Admin-Key", scheme.GetProperty("name").GetString());
    }
}
