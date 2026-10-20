using WebhookGateway.Contracts.Ids;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.Dispatcher.Endpoints;

/// <summary>What the Dispatcher needs to call one subscriber.</summary>
public sealed record EndpointTarget(EndpointId EndpointId, Uri Url, IReadOnlyList<SigningSecret> Secrets);
