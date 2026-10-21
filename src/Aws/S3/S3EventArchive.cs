using System.IO.Compression;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.FanOut.Archive;

namespace WebhookGateway.Aws.S3;

/// <summary>
/// One gzip JSON Lines object per batch under <c>archive/region=/date=/hour=/</c> (docs/data-plane.md section 5).
/// The returned pointer names the object and the line of each event.
/// </summary>
public sealed partial class S3EventArchive(IAmazonS3 s3, TimeProvider clock, IOptions<ArchiveOptions> options, ILogger<S3EventArchive> logger)
    : IEventArchive
{
    private readonly ArchiveOptions _options = options.Value;
    private readonly string _workerId = Guid.NewGuid().ToString("N")[..12];
    private long _sequence;

    public async ValueTask<IReadOnlyDictionary<EventId, string>> WriteBatchAsync(
        IReadOnlyList<EventMessage> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        var now = clock.GetUtcNow();
        var key = $"archive/region={_options.Region}/date={now:yyyy-MM-dd}/hour={now:HH}/{_workerId}-{Interlocked.Increment(ref _sequence)}.jsonl.gz";

        var refs = new Dictionary<EventId, string>(events.Count);
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            for (var line = 0; line < events.Count; line++)
            {
                await writer.WriteLineAsync(ContractJson.Serialize(events[line])).ConfigureAwait(false);
                refs[events[line].EventId] = new ArchiveRef(_options.Bucket, key, line).ToString();
            }
        }

        var size = compressed.Length;
        compressed.Position = 0;
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = compressed,
            ContentType = "application/gzip",
        };
        if (_options.KmsKeyId is not null)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
            request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
        }

        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        LogWritten(logger, events.Count, size, key);
        return refs;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived {EventCount} events ({Bytes} bytes compressed) to {Key}")]
    private static partial void LogWritten(ILogger logger, int eventCount, long bytes, string key);
}
