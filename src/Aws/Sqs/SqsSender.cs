using Amazon.SQS.Model;

namespace WebhookGateway.Aws.Sqs;

/// <summary>Sends bodies to a queue in batches of 10 and throws if any entry was not stored.</summary>
internal static class SqsSender
{
    private const int MaxBatch = 10;

    public static async ValueTask SendAsync(
        SqsQueues queues,
        string queueName,
        IReadOnlyList<(string Body, int DelaySeconds)> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var url = await queues.UrlAsync(queueName, cancellationToken).ConfigureAwait(false);
        for (var offset = 0; offset < messages.Count; offset += MaxBatch)
        {
            var entries = messages
                .Skip(offset)
                .Take(MaxBatch)
                .Select((m, i) => new SendMessageBatchRequestEntry(i.ToString(), m.Body) { DelaySeconds = m.DelaySeconds })
                .ToList();
            var response = await queues.Client.SendMessageBatchAsync(
                new SendMessageBatchRequest { QueueUrl = url, Entries = entries },
                cancellationToken).ConfigureAwait(false);
            if (response.Failed.Count > 0)
            {
                var first = response.Failed[0];
                throw new InvalidOperationException(
                    $"SQS rejected {response.Failed.Count} of {entries.Count} messages for '{queueName}': {first.Code} {first.Message}");
            }
        }
    }
}
