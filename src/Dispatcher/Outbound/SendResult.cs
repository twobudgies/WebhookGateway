namespace WebhookGateway.Dispatcher.Outbound;

public enum SendOutcome
{
    /// <summary>The subscriber answered 2xx.</summary>
    Succeeded,

    /// <summary>Worth trying again: timeout, connection error, 408, 429, 5xx.</summary>
    Retry,

    /// <summary>Do not retry: other 4xx, redirects, blocked address, invalid URL.</summary>
    Failed,
}

/// <param name="Outcome">How the attempt ended.</param>
/// <param name="StatusCode">HTTP status when a response arrived.</param>
/// <param name="ResponseMs">Time from sending the request to the response headers (or to the failure).</param>
/// <param name="Error">Short machine-readable reason for non-success (never a payload or secret).</param>
/// <param name="RetryAfter">Subscriber's <c>Retry-After</c> value, when given.</param>
/// <param name="EndpointGone">The subscriber answered 410: it asked us to stop; the control plane may disable the endpoint.</param>
public sealed record SendResult(
    SendOutcome Outcome,
    int? StatusCode,
    long ResponseMs,
    string? Error,
    TimeSpan? RetryAfter = null,
    bool EndpointGone = false);
