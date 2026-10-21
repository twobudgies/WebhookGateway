using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Aws.S3;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>); skipped when Moto is not reachable.</summary>
public sealed class S3ArchiveTests
{
    private static readonly Uri Moto = new("http://localhost:5050");

    private static async Task<IAmazonS3?> ConnectAsync()
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

        return new AmazonS3Client(
            new BasicAWSCredentials("local", "local"),
            new AmazonS3Config { ServiceURL = Moto.ToString(), AuthenticationRegion = "ap-south-1", ForcePathStyle = true });
    }

    private static EventMessage NewEvent(int n) => new(
        EventMessage.CurrentSchemaVersion,
        EventId.New(),
        TenantId.New(),
        "order.created",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        JsonDocument.Parse($$"""{"n":{{n}}}""").RootElement.Clone());

    [Fact]
    public async Task Archived_events_can_be_read_back_through_their_payload_ref()
    {
        var s3 = await ConnectAsync();
        if (s3 is null)
        {
            return;
        }

        var options = Options.Create(new ArchiveOptions());
        var archive = new S3EventArchive(s3, new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero)), options, NullLogger<S3EventArchive>.Instance);
        var events = new[] { NewEvent(0), NewEvent(1), NewEvent(2) };

        var refs = await archive.WriteBatchAsync(events, CancellationToken.None);

        Assert.StartsWith("s3://webhookgateway-dev/archive/region=ap-south-1/date=2026-10-21/hour=08/", refs[events[0].EventId], StringComparison.Ordinal);
        var resolver = new S3PayloadResolver(s3, NullLogger<S3PayloadResolver>.Instance);
        var target = events[2];
        var delivery = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion,
            target.TenantId,
            target.EventId,
            DeliveryId.For(target.EventId, EndpointId.New()),
            EndpointId.New(),
            target.Type,
            target.OccurredAt,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            refs[target.EventId]);

        var payload = await resolver.ResolveAsync(delivery, CancellationToken.None);

        Assert.Equal(2, payload!.Value.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task A_reference_to_a_missing_object_resolves_to_null()
    {
        var s3 = await ConnectAsync();
        if (s3 is null)
        {
            return;
        }

        var resolver = new S3PayloadResolver(s3, NullLogger<S3PayloadResolver>.Instance);
        var e = NewEvent(0);
        var delivery = new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, e.TenantId, e.EventId, DeliveryId.For(e.EventId, EndpointId.New()), EndpointId.New(),
            e.Type, e.OccurredAt, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "s3://webhookgateway-dev/archive/nope.jsonl.gz#0");

        Assert.Null(await resolver.ResolveAsync(delivery, CancellationToken.None));
    }
}
