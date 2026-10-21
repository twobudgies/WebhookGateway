using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Postgres;
using WebhookGateway.Postgres.Backend;

namespace WebhookGateway.IntegrationTests.Postgres;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>, PostgreSQL on 5432); skipped when it is not reachable.</summary>
[Collection("postgres-queues")]
public sealed class PostgresBackendTests
{
    public const string Connection = "Host=localhost;Port=5432;Database=webhook;Username=webhook;Password=webhook;Timeout=2";
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    internal static async Task<NpgsqlDataSource?> ConnectAsync()
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
            await using var clear = db.CreateCommand("DELETE FROM webhook.queue_messages");
            await clear.ExecuteNonQueryAsync();
            return db;
        }
        catch (NpgsqlException)
        {
            await db.DisposeAsync();
            return null;
        }
    }

    private static PostgresMessageStore Messages(NpgsqlDataSource db) => new(db, NullLogger<PostgresMessageStore>.Instance);

    [Fact]
    public async Task A_delayed_message_is_invisible_until_due_and_a_deleted_message_is_gone()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var store = Messages(db);
        await store.SendAsync("t-delay", [("later", TimeSpan.FromMilliseconds(800)), ("now", TimeSpan.Zero)], CancellationToken.None);

        var first = await store.ReceiveAsync("t-delay", 10, TimeSpan.FromSeconds(30), TimeSpan.Zero, CancellationToken.None);
        await Task.Delay(900);
        var second = await store.ReceiveAsync("t-delay", 10, TimeSpan.FromSeconds(30), TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("now", Assert.Single(first).Body);
        Assert.Equal("later", Assert.Single(second).Body);
        await store.DeleteAsync(first[0], CancellationToken.None);
        await store.DeleteAsync(second[0], CancellationToken.None);
        Assert.Empty(await store.ReceiveAsync("t-delay", 10, TimeSpan.FromMilliseconds(1), TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task An_unacknowledged_message_reappears_and_moves_to_the_dead_letter_queue_after_five_receives()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var store = Messages(db);
        await store.SendAsync("t-dlq", [("poison", TimeSpan.Zero)], CancellationToken.None);

        for (var i = 1; i <= PostgresMessageStore.MaxReceives; i++)
        {
            var got = await store.ReceiveAsync("t-dlq", 1, TimeSpan.FromMilliseconds(50), TimeSpan.Zero, CancellationToken.None);
            Assert.Equal(i, Assert.Single(got).ReceiveCount);
            await Task.Delay(80);
        }

        Assert.Empty(await store.ReceiveAsync("t-dlq", 1, TimeSpan.FromSeconds(30), TimeSpan.Zero, CancellationToken.None));
        var dead = await store.ReceiveAsync("t-dlq-dlq", 1, TimeSpan.FromSeconds(30), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal("poison", Assert.Single(dead).Body);
    }

    [Fact]
    public async Task Two_workers_never_receive_the_same_message()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var store = Messages(db);
        await store.SendAsync("t-race", [.. Enumerable.Range(0, 40).Select(i => ($"m{i}", TimeSpan.Zero))], CancellationToken.None);

        var batches = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            store.ReceiveAsync("t-race", 10, TimeSpan.FromSeconds(30), TimeSpan.Zero, CancellationToken.None).AsTask()));

        var all = batches.SelectMany(b => b).Select(m => m.Body).ToList();
        Assert.Equal(40, all.Count);
        Assert.Equal(40, all.Distinct().Count());
    }

    [Fact]
    public async Task Delivery_state_round_trips_and_only_due_scheduled_rows_are_in_the_index()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var store = new PostgresDeliveryStateStore(db, new FakeTimeProvider(Now), Options.Create(new BackendOptions()), NullLogger<PostgresDeliveryStateStore>.Instance);
        DeliveryStateRecord Record(DeliveryStatus status, DateTimeOffset next)
        {
            var eventId = EventId.New();
            var endpointId = EndpointId.New();
            return new DeliveryStateRecord(
                TenantId.New(), DeliveryId.For(eventId, endpointId), endpointId, eventId, "order.created", Now.AddMinutes(-5), status, 3,
                next, 503, "http_503", JsonDocument.Parse("""{"a":1}""").RootElement.Clone(), null, Now.AddMinutes(-4), Now, PlanTier.Premium);
        }

        var due = Record(DeliveryStatus.Scheduled, Now.AddMinutes(-1));
        var later = Record(DeliveryStatus.Scheduled, Now.AddHours(3));
        var other = Record(DeliveryStatus.RetryScheduled, Now.AddMinutes(-1));
        foreach (var r in new[] { due, later, other })
        {
            await store.PutAsync(r, CancellationToken.None);
        }

        var read = await store.GetAsync(due.TenantId, due.DeliveryId, CancellationToken.None);
        var dueNow = await store.GetDueAsync(Now, 1000, CancellationToken.None);
        await store.DeleteAsync(due.TenantId, due.DeliveryId, CancellationToken.None);

        Assert.Equal(PlanTier.Premium, read!.Tier);
        Assert.Equal(1, read.Payload!.Value.GetProperty("a").GetInt32());
        Assert.Contains(dueNow, r => r.DeliveryId == due.DeliveryId);
        Assert.DoesNotContain(dueNow, r => r.DeliveryId == later.DeliveryId || r.DeliveryId == other.DeliveryId);
        Assert.Null(await store.GetAsync(due.TenantId, due.DeliveryId, CancellationToken.None));
    }

    [Fact]
    public async Task Rollups_from_two_flushes_are_added_together()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var writer = new PostgresRollupWriter(db, new FakeTimeProvider(Now), Options.Create(new BackendOptions()), NullLogger<PostgresRollupWriter>.Instance);
        var tenant = TenantId.New();
        var endpoint = EndpointId.New();
        var accumulator = new RollupAccumulator();
        for (var i = 0; i < 2; i++)
        {
            accumulator.RecordAttempt(new AttemptSample(tenant, endpoint, DeliveryOutcome.Succeeded, 10, 40, false, Now));
            Assert.Empty(await writer.WriteAsync(accumulator.Drain(), CancellationToken.None));
        }

        await using var read = db.CreateCommand("SELECT attempts, succeeded, rt_sum FROM webhook.rollups WHERE pk = @pk AND sk = '1m#202610211200'");
        read.Parameters.AddWithValue("pk", $"{tenant.Value}#{endpoint.Value}");
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((2L, 2L, 80L), (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
    }

    [Fact]
    public async Task Archived_events_are_read_back_by_their_pointer()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var archive = new PostgresEventArchive(db, new FakeTimeProvider(Now), Options.Create(new BackendOptions()), NullLogger<PostgresEventArchive>.Instance);
        EventMessage NewEvent(int n) => new(
            EventMessage.CurrentSchemaVersion, EventId.New(), TenantId.New(), "order.created", Now, Now, null, JsonDocument.Parse($$"""{"n":{{n}}}""").RootElement.Clone());
        var events = new[] { NewEvent(0), NewEvent(1), NewEvent(2) };

        var refs = await archive.WriteBatchAsync(events, CancellationToken.None);
        var target = events[2];
        var delivery = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, target.TenantId, target.EventId, DeliveryId.For(target.EventId, EndpointId.New()), EndpointId.New(),
            target.Type, Now, 1, Now, Now, null, refs[target.EventId]);
        var payload = await new PostgresPayloadResolver(db, NullLogger<PostgresPayloadResolver>.Instance).ResolveAsync(delivery, CancellationToken.None);
        var missing = await new PostgresPayloadResolver(db, NullLogger<PostgresPayloadResolver>.Instance)
            .ResolveAsync(delivery with { PayloadRef = "pg://archive/nope#0" }, CancellationToken.None);

        Assert.Equal(2, payload!.Value.GetProperty("n").GetInt32());
        Assert.Null(missing);
    }

    [Fact]
    public async Task Idempotency_keys_and_the_monthly_count_are_durable_and_exact()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var idempotency = new PostgresIdempotencyStore(db);
        var quota = new PostgresQuotaCounter(db);
        var tenant = TenantId.New();
        var first = EventId.New();

        var a = await idempotency.TryClaimAsync(tenant, "k", first, TimeSpan.FromMinutes(5), CancellationToken.None);
        var b = await idempotency.TryClaimAsync(tenant, "k", EventId.New(), TimeSpan.FromMinutes(5), CancellationToken.None);
        await idempotency.ReleaseAsync(tenant, "k", CancellationToken.None);
        var c = await idempotency.TryClaimAsync(tenant, "k", EventId.New(), TimeSpan.FromMinutes(5), CancellationToken.None);
        await quota.IncrementAsync(tenant, Now, CancellationToken.None);
        var two = await quota.IncrementAsync(tenant, Now, CancellationToken.None);
        await quota.DecrementAsync(tenant, Now, CancellationToken.None);
        var again = await quota.IncrementAsync(tenant, Now, CancellationToken.None);

        Assert.True(a.Claimed);
        Assert.False(b.Claimed);
        Assert.Equal(first, b.ExistingEventId);
        Assert.True(c.Claimed);
        Assert.Equal(2, two);
        Assert.Equal(2, again);
    }

    [Fact]
    public async Task Maintenance_removes_expired_rows()
    {
        await using var db = await ConnectAsync();
        if (db is null)
        {
            return;
        }

        var tenant = TenantId.New();
        await using (var insert = db.CreateCommand("INSERT INTO webhook.idempotency_keys (tenant_id, key, event_id, expires_at) VALUES (@t, 'old', 'evt_x', now() - interval '1 minute')"))
        {
            insert.Parameters.AddWithValue("t", tenant.Value);
            await insert.ExecuteNonQueryAsync();
        }

        var removed = await new PostgresMaintenance(db, NullLogger<PostgresMaintenance>.Instance).RunOnceAsync(CancellationToken.None);

        Assert.True(removed >= 1);
        var claim = await new PostgresIdempotencyStore(db).TryClaimAsync(tenant, "old", EventId.New(), TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(claim.Claimed);
    }
}
