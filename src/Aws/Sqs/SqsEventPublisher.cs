using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Ingest.Publishing;

namespace WebhookGateway.Aws.Sqs;

public sealed class SqsEventPublisher(SqsQueues queues) : IEventPublisher
{
    public ValueTask PublishAsync(EventMessage message, CancellationToken cancellationToken) =>
        SqsSender.SendAsync(queues, queues.Names.Events, [(ContractJson.Serialize(message), 0)], cancellationToken);
}
