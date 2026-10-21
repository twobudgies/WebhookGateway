using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.Hosting;
using WebhookGateway.IntegrationTests.Ingest;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Ingest started with <c>Backend:Mode=Aws</c> puts accepted events on the real SQS queue. Needs <c>./scripts/dev-up.sh</c>.</summary>
[Collection("moto-queues")]
public sealed class IngestAwsWiringTests : IClassFixture<IngestFactory>
{
    private static readonly Uri Moto = new("http://localhost:5050");
    private readonly IngestFactory _factory;

    public IngestAwsWiringTests(IngestFactory factory) => _factory = factory;

    [Fact]
    public async Task An_accepted_event_lands_on_the_sqs_events_queue()
    {
        using var sqs = new AmazonSQSClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = Moto.ToString(), AuthenticationRegion = "ap-south-1" });
        string queueUrl;
        try
        {
            queueUrl = (await sqs.GetQueueUrlAsync("events")).QueueUrl;
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            return; // local stack not running
        }

        await sqs.PurgeQueueAsync(queueUrl);
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads Backend:Mode before late configuration is applied.
        using var aws = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Backend:Mode", "Aws");
            builder.UseSetting("Aws:ServiceUrl", Moto.ToString());
        });
        using var client = aws.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IngestFactory.ActiveKey);
        using var content = new StringContent("""{"type":"order.created","data":{"a":1}}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/v1/events", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, WaitTimeSeconds = 2 });
        var body = Assert.Single(received.Messages!).Body;
        Assert.Contains("order.created", body, StringComparison.Ordinal);
    }
}
