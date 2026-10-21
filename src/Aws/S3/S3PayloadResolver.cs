using System.IO.Compression;
using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.Aws.S3;

/// <summary>Inline payload when present, otherwise the event is read back from the archive object named by <c>PayloadRef</c>.</summary>
public sealed partial class S3PayloadResolver(IAmazonS3 s3, ILogger<S3PayloadResolver> logger) : IPayloadResolver
{
    public async ValueTask<JsonElement?> ResolveAsync(DeliveryMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload is not null)
        {
            return message.Payload;
        }

        if (!ArchiveRef.TryParse(message.PayloadRef, out var reference))
        {
            LogBadRef(logger, message.DeliveryId.Value);
            return null;
        }

        try
        {
            using var response = await s3.GetObjectAsync(reference.Bucket, reference.Key, cancellationToken).ConfigureAwait(false);
            await using var gzip = new GZipStream(response.ResponseStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            for (var line = 0; await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } text; line++)
            {
                if (line == reference.Line)
                {
                    var stored = ContractJson.Deserialize<EventMessage>(text);
                    return stored.EventId == message.EventId ? stored.Payload : Mismatch(message);
                }
            }

            LogMissing(logger, message.DeliveryId.Value, reference.Key, reference.Line);
            return null;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LogMissing(logger, message.DeliveryId.Value, reference.Key, reference.Line);
            return null;
        }
    }

    private JsonElement? Mismatch(DeliveryMessage message)
    {
        LogMismatch(logger, message.DeliveryId.Value, message.EventId.Value);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId} has an unreadable payload reference")]
    private static partial void LogBadRef(ILogger logger, string deliveryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId}: archive object {Key} line {Line} not found")]
    private static partial void LogMissing(ILogger logger, string deliveryId, string key, int line);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId}: archive line does not belong to event {EventId}")]
    private static partial void LogMismatch(ILogger logger, string deliveryId, string eventId);
}
