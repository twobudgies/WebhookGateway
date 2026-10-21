using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Aws.DynamoDb;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>); skipped when Moto is not reachable.</summary>
public sealed class DynamoDbTests
{
    private static readonly Uri Moto = new("http://localhost:5050");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 30, 15, TimeSpan.Zero);

    private static async Task<IAmazonDynamoDB?> ConnectAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            await http.GetAsync(Moto);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }

        return new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = Moto.ToString(), AuthenticationRegion = "ap-south-1" });
    }

    private static DeliveryStateRecord Record(DeliveryStatus status, DateTimeOffset? next = null)
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        return new DeliveryStateRecord(
            TenantId.New(), DeliveryId.For(eventId, endpointId), endpointId, eventId, "order.created", Now.AddMinutes(-5), status, 2,
            next, 503, "http_503", JsonDocument.Parse("""{"a":1}""").RootElement.Clone(), null, Now.AddMinutes(-4), Now, PlanTier.Basic);
    }

    private static DynamoDbDeliveryStateStore Store(IAmazonDynamoDB dynamo) =>
        new(dynamo, new FakeTimeProvider(Now), Options.Create(new DynamoDbOptions()), NullLogger<DynamoDbDeliveryStateStore>.Instance);

    [Fact]
    public async Task A_delivery_row_round_trips_and_can_be_deleted()
    {
        var dynamo = await ConnectAsync();
        if (dynamo is null)
        {
            return;
        }

        var store = Store(dynamo);
        var record = Record(DeliveryStatus.Scheduled, Now.AddHours(1));

        await store.PutAsync(record, CancellationToken.None);
        var read = await store.GetAsync(record.TenantId, record.DeliveryId, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(record.Status, read.Status);
        Assert.Equal(record.Attempt, read.Attempt);
        Assert.Equal(record.NextAttemptAt, read.NextAttemptAt);
        Assert.Equal(503, read.LastStatusCode);
        Assert.Equal(1, read.Payload!.Value.GetProperty("a").GetInt32());

        await store.DeleteAsync(record.TenantId, record.DeliveryId, CancellationToken.None);
        Assert.Null(await store.GetAsync(record.TenantId, record.DeliveryId, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_row_reads_as_null()
    {
        var dynamo = await ConnectAsync();
        if (dynamo is null)
        {
            return;
        }

        var record = Record(DeliveryStatus.Failed);

        Assert.Null(await Store(dynamo).GetAsync(record.TenantId, record.DeliveryId, CancellationToken.None));
    }

    [Fact]
    public async Task Rollups_from_two_flushes_are_added_together()
    {
        var dynamo = await ConnectAsync();
        if (dynamo is null)
        {
            return;
        }

        var writer = new DynamoDbRollupWriter(dynamo, new FakeTimeProvider(Now), Options.Create(new DynamoDbOptions()), NullLogger<DynamoDbRollupWriter>.Instance);
        var tenant = TenantId.New();
        var endpoint = EndpointId.New();
        var accumulator = new RollupAccumulator();

        for (var flush = 0; flush < 2; flush++)
        {
            accumulator.RecordAttempt(new AttemptSample(tenant, endpoint, DeliveryOutcome.Succeeded, 10, 40, false, Now));
            Assert.Empty(await writer.WriteAsync(accumulator.Drain(), CancellationToken.None));
        }

        var item = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = "rollups",
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new() { S = $"{tenant.Value}#{endpoint.Value}" },
                ["sk"] = new() { S = "1m#202610210830" },
            },
        });
        Assert.Equal("2", item.Item["attempts"].N);
        Assert.Equal("2", item.Item["succeeded"].N);
        Assert.Equal("80", item.Item["rtSum"].N);
    }

    [Fact]
    public async Task Only_scheduled_deliveries_that_are_due_come_back_from_the_index()
    {
        var dynamo = await ConnectAsync();
        if (dynamo is null)
        {
            return;
        }

        var store = Store(dynamo);
        var due = Record(DeliveryStatus.Scheduled, Now.AddMinutes(-1));
        var later = Record(DeliveryStatus.Scheduled, Now.AddHours(3));
        var notScheduled = Record(DeliveryStatus.RetryScheduled, Now.AddMinutes(-1));
        foreach (var r in new[] { due, later, notScheduled })
        {
            await store.PutAsync(r, CancellationToken.None);
        }

        var result = await store.GetDueAsync(Now, 1000, CancellationToken.None);

        Assert.Contains(result, r => r.DeliveryId == due.DeliveryId && r.Tier == PlanTier.Basic);
        Assert.DoesNotContain(result, r => r.DeliveryId == later.DeliveryId);
        Assert.DoesNotContain(result, r => r.DeliveryId == notScheduled.DeliveryId);
    }
}
