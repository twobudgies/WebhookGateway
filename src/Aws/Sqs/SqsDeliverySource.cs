using System.Text.Json;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Source;

namespace WebhookGateway.Aws.Sqs;

public sealed partial class SqsDeliverySource(SqsQueues queues, ILogger<SqsDeliverySource> logger, int waitSeconds = 20) : IDeliverySource
{
    public async ValueTask<IReadOnlyList<ReceivedDelivery>> ReceiveAsync(PlanTier tier, int maxCount, CancellationToken cancellationToken)
    {
        var url = await queues.UrlAsync(SqsQueueNames.Deliveries(tier), cancellationToken).ConfigureAwait(false);
        var response = await queues.Client.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = url,
                MaxNumberOfMessages = Math.Clamp(maxCount, 1, 10),
                WaitTimeSeconds = waitSeconds,
            },
            cancellationToken).ConfigureAwait(false);

        var deliveries = new List<ReceivedDelivery>();
        foreach (var m in response.Messages ?? [])
        {
            try
            {
                deliveries.Add(new ReceivedDelivery(ContractJson.Deserialize<DeliveryMessage>(m.Body ?? string.Empty), m.ReceiptHandle));
            }
            catch (JsonException ex)
            {
                // Not acknowledged: SQS redelivers it and moves it to the DLQ after maxReceiveCount. The body is never logged.
                LogUnreadable(logger, ex, m.MessageId, tier);
            }
        }

        return deliveries;
    }

    public async ValueTask AcknowledgeAsync(PlanTier tier, ReceivedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var url = await queues.UrlAsync(SqsQueueNames.Deliveries(tier), cancellationToken).ConfigureAwait(false);
        await queues.Client.DeleteMessageAsync(url, delivery.Receipt, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable message {MessageId} left on the {Tier} delivery queue for redelivery and the DLQ")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string messageId, PlanTier tier);
}
