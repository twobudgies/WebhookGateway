using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>
/// One signed POST to a subscriber and the decision of what the result means. It never throws for subscriber
/// problems; only a shutdown (caller's cancellation) surfaces as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class WebhookSender(HttpClient http, TimeProvider clock, IOptions<SenderOptions> options) : IWebhookSender
{
    /// <summary>Longest <c>Retry-After</c> we honor.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private readonly SenderOptions _options = options.Value;

    public async ValueTask<SendResult> SendAsync(
        EndpointTarget target,
        DeliveryMessage message,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);

        var urlProblem = EndpointUrlValidator.Validate(target.Url, _options.AllowInsecureHttp);
        if (urlProblem is not null)
        {
            return new SendResult(SendOutcome.Failed, null, 0, "url_not_allowed");
        }

        var body = OutboundBody.Build(message, payload);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(WebhookSigner.IdHeader, message.DeliveryId.Value);
        request.Headers.TryAddWithoutValidation(WebhookSigner.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebhookSigner.SignatureHeader, WebhookSigner.Sign(message.DeliveryId.Value, timestamp, body, target.Secrets));
        request.Headers.UserAgent.ParseAdd("WebhookGateway/1.0");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        var started = clock.GetTimestamp();
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return Classify(response, ElapsedMs(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendResult(SendOutcome.Retry, null, ElapsedMs(started), "timeout");
        }
        catch (HttpRequestException ex)
        {
            return IsBlocked(ex)
                ? new SendResult(SendOutcome.Failed, null, ElapsedMs(started), "blocked_address")
                : new SendResult(SendOutcome.Retry, null, ElapsedMs(started), "connection_error");
        }
    }

    private long ElapsedMs(long startedTimestamp) => (long)clock.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    private static bool IsBlocked(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }

    private SendResult Classify(HttpResponseMessage response, long elapsedMs)
    {
        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300)
        {
            return new SendResult(SendOutcome.Succeeded, status, elapsedMs, null);
        }

        if (status == (int)HttpStatusCode.Gone)
        {
            return new SendResult(SendOutcome.Failed, status, elapsedMs, "endpoint_gone", EndpointGone: true);
        }

        if (status is 408 or 429 or >= 500)
        {
            return new SendResult(SendOutcome.Retry, status, elapsedMs, $"http_{status}", ReadRetryAfter(response));
        }

        // 3xx (redirects are never followed) and every other 4xx will not succeed by repeating the same request.
        return new SendResult(SendOutcome.Failed, status, elapsedMs, $"http_{status}");
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        var delay = header.Delta ?? (header.Date is { } date ? date - clock.GetUtcNow() : null);
        if (delay is null)
        {
            return null;
        }

        var value = delay.Value;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxRetryAfter ? MaxRetryAfter : value;
    }
}
