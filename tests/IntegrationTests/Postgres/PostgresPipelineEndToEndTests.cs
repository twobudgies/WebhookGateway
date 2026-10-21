using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Aws;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;
using WebhookGateway.Dispatcher.Source;
using WebhookGateway.FanOut;
using WebhookGateway.FanOut.Routing;
using WebhookGateway.Ingest;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Postgres;
using WebhookGateway.Postgres.Backend;

namespace WebhookGateway.IntegrationTests.Postgres;

/// <summary>
/// The whole product on PostgreSQL alone (Backend:Mode=Postgres, no AWS emulator): a tenant, key and endpoint in PostgreSQL; an event posted to Ingest lands on SQS;
/// Fan-out archives it to S3 and routes it using the config database; the Dispatcher reads the delivery queue, loads the
/// endpoint and its decrypted secret, and "sends" through a recording sender (real HTTP is covered elsewhere and
/// loopback targets are blocked on purpose). Needs <c>./scripts/dev-up.sh</c> (PostgreSQL); skipped otherwise.
/// </summary>
[Collection("postgres-queues")]
public sealed class PostgresPipelineEndToEndTests
{
    private const string Connection = "Host=localhost;Port=5432;Database=webhook;Username=webhook;Password=webhook;Timeout=2";

    [Fact]
    public async Task An_event_posted_to_ingest_is_delivered_to_the_tenants_endpoint()
    {
        await using var db = NpgsqlDataSource.Create(Connection);
        try
        {
            await using var probe = await db.OpenConnectionAsync();
        }
        catch (NpgsqlException)
        {
            return; // local stack not running
        }

        var localKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = LocalSecretProtector.FromBase64(localKey);
        var tenant = Guid.CreateVersion7();
        var endpoint = Guid.CreateVersion7();
        var apiKey = $"wgk_e2e_{Guid.NewGuid():N}";
        var signing = WebhookGateway.Dispatcher.Signing.SigningSecret.Generate();
        var stored = await protector.ProtectAsync(signing.ToText(), CancellationToken.None);
        await Exec(db, "INSERT INTO webhook.tenants (id, name, plan_code) VALUES (@id, 'E2E', 'advanced')", ("id", tenant));
        await Exec(db, "INSERT INTO webhook.api_keys (id, tenant_id, key_hash, prefix) VALUES (@id, @t, @h, 'wgk_e2e_')", ("id", Guid.CreateVersion7()), ("t", tenant), ("h", ApiKeyHasher.Hash(apiKey)));
        await using (var insert = db.CreateCommand("INSERT INTO webhook.endpoints (id, tenant_id, url, secret_ciphertext, secret_kms_key_id) VALUES (@id, @t, 'https://customer.example.com/hook', @c, @k)"))
        {
            insert.Parameters.AddWithValue("id", endpoint);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("c", stored.Ciphertext);
            insert.Parameters.AddWithValue("k", stored.KeyId);
            await insert.ExecuteNonQueryAsync();
        }

        await Exec(db, "INSERT INTO webhook.subscriptions (id, tenant_id, endpoint_id, event_type) VALUES (@id, @t, @e, 'order.created')", ("id", Guid.CreateVersion7()), ("t", tenant), ("e", endpoint));

        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["Backend:Mode"] = "Postgres",
                ["Postgres:ConnectionString"] = Connection,
                ["Secrets:LocalKey"] = localKey,
            };
            await using (var clear = db.CreateCommand("DELETE FROM webhook.queue_messages"))
            {
                await clear.ExecuteNonQueryAsync();
            }


            // 1. Ingest accepts the event.
            using var ingest = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<IngestApiMarker>().WithWebHostBuilder(b =>
            {
                foreach (var (k, v) in settings)
                {
                    b.UseSetting(k, v);
                }
            });
            using var client = ingest.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var content = new StringContent("""{"type":"order.created","data":{"orderId":42}}""", Encoding.UTF8, "application/json");
            using var accepted = await client.PostAsync(new Uri("/v1/events", UriKind.Relative), content);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var eventId = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement.GetProperty("eventId").GetString()!;

            // 2. Fan-out routes it using the config database.
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            await using var fanOut = BuildFanOut(configuration);
            Assert.Equal(1, await fanOut.GetRequiredService<EventConsumer>().RunOnceAsync(CancellationToken.None));

            // 3. The Dispatcher delivers it to the endpoint.
            var sender = new RecordingSender();
            await using var dispatcher = BuildDispatcher(configuration, sender);
            var consumer = dispatcher.GetRequiredService<DeliveryConsumer>();
            Assert.Equal(1, await consumer.RunOnceAsync(PlanTier.Advanced, CancellationToken.None));

            var call = Assert.Single(sender.Calls);
            Assert.Equal($"ep_{endpoint}", call.Target.EndpointId.Value);
            Assert.Equal(new Uri("https://customer.example.com/hook"), call.Target.Url);
            Assert.Equal(signing.ToText(), Assert.Single(call.Target.Secrets).ToText());
            Assert.Equal(eventId, call.Message.EventId.Value);
            Assert.Equal(42, call.Payload.GetProperty("orderId").GetInt32());
        }
        finally
        {
            await Exec(db, "DELETE FROM webhook.tenants WHERE id = @id", ("id", tenant));
        }
    }

    private static ServiceProvider BuildFanOut(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RollupAccumulator>();
        services.AddPostgresDataSource(configuration);
        services.AddSingleton<ISubscriptionStore, PostgresSubscriptionStore>();
        services.AddPostgresFanOut(configuration);
        services.AddSingleton<WebhookGateway.FanOut.Processing.FanOutProcessor>();
        services.AddSingleton<EventConsumer>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildDispatcher(IConfiguration configuration, RecordingSender sender)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RollupAccumulator>();
        services.AddSingleton(RetryPolicy.CreateDefault());
        services.Configure<DeliveryOptions>(_ => { });
        services.Configure<DeliveryConsumerOptions>(_ => { });
        services.AddPostgresDataSource(configuration);
        services.AddSecretProtection(configuration);
        services.AddSingleton<IEndpointStore, PostgresEndpointStore>();
        services.AddPostgresDispatcher(configuration);
        services.AddSingleton<IWebhookSender>(sender);
        services.AddSingleton<DeliveryProcessor>();
        services.AddSingleton<IDeliveryProcessor>(sp => sp.GetRequiredService<DeliveryProcessor>());
        services.AddSingleton<DeliveryConsumer>();
        return services.BuildServiceProvider();
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

    private sealed class RecordingSender : IWebhookSender
    {
        public List<(EndpointTarget Target, DeliveryMessage Message, JsonElement Payload)> Calls { get; } = [];

        public ValueTask<SendResult> SendAsync(EndpointTarget target, DeliveryMessage message, JsonElement payload, CancellationToken cancellationToken)
        {
            Calls.Add((target, message, payload));
            return ValueTask.FromResult(new SendResult(SendOutcome.Succeeded, 200, 12, null));
        }
    }
}
