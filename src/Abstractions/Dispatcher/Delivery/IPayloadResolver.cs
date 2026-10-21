using System.Text.Json;
using WebhookGateway.Contracts.Messages;

namespace WebhookGateway.Dispatcher.Delivery;

/// <summary>Finds the payload of a delivery: inline when present, otherwise from the archive via <c>PayloadRef</c>.</summary>
public interface IPayloadResolver
{
    /// <returns>The payload, or null when the reference points to nothing. Transient storage errors throw.</returns>
    ValueTask<JsonElement?> ResolveAsync(DeliveryMessage message, CancellationToken cancellationToken);
}
