using System.Text.Json;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Endpoints;

namespace WebhookGateway.Dispatcher.Outbound;

public interface IWebhookSender
{
    ValueTask<SendResult> SendAsync(EndpointTarget target, DeliveryMessage message, JsonElement payload, CancellationToken cancellationToken);
}
