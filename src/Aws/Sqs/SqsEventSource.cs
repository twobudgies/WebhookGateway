using System.Text.Json;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.FanOut.Source;

namespace WebhookGateway.Aws.Sqs;

public sealed partial class SqsEventSource(SqsQueues queues, ILogger<SqsEventSource> logger, int waitSeconds = 20) : IEventSource
{
    public async ValueTask<IReadOnlyList<ReceivedEvent>> ReceiveAsync(int maxCount, CancellationToken cancellationToken)
    {
        var url = await queues.UrlAsync(queues.Names.Events, cancellationToken).ConfigureAwait(false);
        var response = await queues.Client.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = url,
                MaxNumberOfMessages = Math.Clamp(maxCount, 1, 10),
                WaitTimeSeconds = waitSeconds,
            },
            cancellationToken).ConfigureAwait(false);
        var events = new List<ReceivedEvent>();
        foreach (var m in response.Messages ?? [])
        {
            try
            {
                events.Add(new ReceivedEvent(ContractJson.Deserialize<EventMessage>(m.Body ?? string.Empty), m.ReceiptHandle));
            }
            catch (JsonException ex)
            {
                // Not acknowledged: SQS redelivers it and moves it to the DLQ after maxReceiveCount. The body is never logged.
                LogUnreadableMessage(logger, ex, m.MessageId, m.Body?.Length ?? 0);
            }
        }

        return events;
    }

    public async ValueTask AcknowledgeAsync(IReadOnlyList<ReceivedEvent> events, CancellationToken cancellationToken)
    {
        var url = await queues.UrlAsync(queues.Names.Events, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in events.Chunk(10))
        {
            var response = await queues.Client.DeleteMessageBatchAsync(
                new DeleteMessageBatchRequest
                {
                    QueueUrl = url,
                    Entries = [.. chunk.Select((e, i) => new DeleteMessageBatchRequestEntry(i.ToString(), e.Receipt))],
                },
                cancellationToken).ConfigureAwait(false);
            if (response.Failed.Count > 0)
            {
                LogAckFailed(logger, response.Failed.Count, chunk.Length);
                throw new InvalidOperationException($"SQS failed to delete {response.Failed.Count} messages: {response.Failed[0].Code}");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable message {MessageId} ({BodyLength} chars) left on the events queue for redelivery and the DLQ")]
    private static partial void LogUnreadableMessage(ILogger logger, Exception exception, string messageId, int bodyLength);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SQS failed to delete {FailedCount} of {Count} acknowledged events; they will be redelivered")]
    private static partial void LogAckFailed(ILogger logger, int failedCount, int count);
}
