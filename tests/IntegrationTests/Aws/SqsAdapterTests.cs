using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Extensions.Logging.Abstractions;
using WebhookGateway.Aws.Sqs;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>); skipped when Moto is not reachable.</summary>
public sealed class SqsAdapterTests
{
    private static readonly Uri Moto = new("http://localhost:5050");

    private static async Task<SqsQueues?> ConnectAsync(string? deliveriesQueue = null)
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

        var client = new AmazonSQSClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = Moto.ToString(), AuthenticationRegion = "ap-south-1" });
        var name = $"it-{Guid.NewGuid():N}";
        await client.CreateQueueAsync(name);
        if (deliveriesQueue is not null)
        {
            await client.CreateQueueAsync(deliveriesQueue);
        }

        return new SqsQueues(client, new SqsQueueNames { Events = name });
    }

    [Fact]
    public async Task Event_published_is_received_and_acknowledged()
    {
        var queues = await ConnectAsync();
        if (queues is null)
        {
            return;
        }

        var sent = new EventMessage(
            EventMessage.CurrentSchemaVersion,
            EventId.New(),
            TenantId.New(),
            "order.created",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "key-1",
            JsonDocument.Parse("""{"a":1}""").RootElement.Clone());

        await new SqsEventPublisher(queues).PublishAsync(sent, CancellationToken.None);
        var source = new SqsEventSource(queues, NullLogger<SqsEventSource>.Instance, waitSeconds: 1);
        var received = await source.ReceiveAsync(10, CancellationToken.None);

        var one = Assert.Single(received);
        Assert.Equal(sent.EventId, one.Message.EventId);
        Assert.Equal("order.created", one.Message.Type);

        await source.AcknowledgeAsync(received, CancellationToken.None);
        Assert.Empty(await source.ReceiveAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Retry_is_delayed_and_stored_on_the_tier_queue()
    {
        var queues = await ConnectAsync("deliveries-basic");
        if (queues is null)
        {
            return;
        }

        var queueUrl = await queues.UrlAsync("deliveries-basic", CancellationToken.None);
        await queues.Client.PurgeQueueAsync(queueUrl);

        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var now = DateTimeOffset.UtcNow;
        var message = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion,
            TenantId.New(),
            eventId,
            DeliveryId.For(eventId, endpointId),
            endpointId,
            "order.created",
            now,
            2,
            now,
            now,
            JsonDocument.Parse("""{"a":1}""").RootElement.Clone(),
            null);

        await new SqsDeliveryPublisher(queues).PublishAsync(PlanTier.Basic, message, TimeSpan.FromSeconds(1), CancellationToken.None);

        var url = await queues.UrlAsync("deliveries-basic", CancellationToken.None);
        var immediate = await queues.Client.ReceiveMessageAsync(new Amazon.SQS.Model.ReceiveMessageRequest { QueueUrl = url, WaitTimeSeconds = 0 });
        Assert.Empty(immediate.Messages ?? []);

        await Task.Delay(1500);
        var later = await queues.Client.ReceiveMessageAsync(new Amazon.SQS.Model.ReceiveMessageRequest { QueueUrl = url, WaitTimeSeconds = 1 });
        var body = Assert.Single(later.Messages!).Body;
        Assert.Equal(message.DeliveryId, ContractJson.Deserialize<DeliveryMessage>(body).DeliveryId);
    }

    [Fact]
    public async Task Delivery_published_is_received_by_tier_and_acknowledged()
    {
        var queues = await ConnectAsync("deliveries-premium");
        if (queues is null)
        {
            return;
        }

        await queues.Client.PurgeQueueAsync(await queues.UrlAsync("deliveries-premium", CancellationToken.None));
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var now = DateTimeOffset.UtcNow;
        var message = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, TenantId.New(), eventId, DeliveryId.For(eventId, endpointId), endpointId,
            "order.created", now, 1, now, now, JsonDocument.Parse("""{"a":1}""").RootElement.Clone(), null);
        await new SqsDeliveryPublisher(queues).PublishAsync(PlanTier.Premium, [message], CancellationToken.None);
        var source = new SqsDeliverySource(queues, NullLogger<SqsDeliverySource>.Instance, waitSeconds: 1);

        var received = await source.ReceiveAsync(PlanTier.Premium, 10, CancellationToken.None);

        var one = Assert.Single(received);
        Assert.Equal(message.DeliveryId, one.Message.DeliveryId);
        await source.AcknowledgeAsync(PlanTier.Premium, one, CancellationToken.None);
        Assert.Empty(await source.ReceiveAsync(PlanTier.Premium, 10, CancellationToken.None));
    }
}
