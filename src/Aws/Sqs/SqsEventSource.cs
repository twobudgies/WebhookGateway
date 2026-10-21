using Amazon.SQS.Model;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.FanOut.Source;

namespace WebhookGateway.Aws.Sqs;

public sealed class SqsEventSource(SqsQueues queues, int waitSeconds = 20) : IEventSource
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
        return [.. (response.Messages ?? []).Select(m => new ReceivedEvent(ContractJson.Deserialize<EventMessage>(m.Body), m.ReceiptHandle))];
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
                throw new InvalidOperationException($"SQS failed to delete {response.Failed.Count} messages: {response.Failed[0].Code}");
            }
        }
    }
}
