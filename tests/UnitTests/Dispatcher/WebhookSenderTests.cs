using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class WebhookSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonElement Payload = JsonDocument.Parse("""{"orderId":42}""").RootElement.Clone();

    private readonly FakeTimeProvider _time = new(Now);
    private readonly SigningSecret _secret = SigningSecret.Generate();

    private EndpointTarget Target(string url = "https://subscriber.example.com/hook") =>
        new(EndpointId.New(), new Uri(url), [_secret]);

    private static DeliveryMessage Message(EndpointTarget target)
    {
        var eventId = EventId.New();
        return new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, TenantId.New(), eventId, DeliveryId.For(eventId, target.EndpointId),
            target.EndpointId, "order.created", Now.AddMinutes(-1), 1, Now, Now, Payload, null);
    }

    private WebhookSender Sender(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, SenderOptions? options = null) =>
        new(new HttpClient(new StubHandler(respond)), _time, Options.Create(options ?? new SenderOptions()));

    private static Task<HttpResponseMessage> Status(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));

    [Fact]
    public async Task Sends_a_signed_json_post_with_the_standard_headers()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var target = Target();
        var message = Message(target);
        var sender = Sender(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await sender.SendAsync(target, message, Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Succeeded, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("application/json", captured.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(message.DeliveryId.Value, captured.Headers.GetValues("webhook-id").Single());
        var timestamp = long.Parse(captured.Headers.GetValues("webhook-timestamp").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Now.ToUnixTimeSeconds(), timestamp);
        var expectedSignature = WebhookSigner.Sign(message.DeliveryId.Value, timestamp, System.Text.Encoding.UTF8.GetBytes(body!), [_secret]);
        Assert.Equal(expectedSignature, captured.Headers.GetValues("webhook-signature").Single());

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(message.EventId.Value, doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("order.created", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(message.OccurredAt, doc.RootElement.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(42, doc.RootElement.GetProperty("data").GetProperty("orderId").GetInt32());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Any_2xx_is_success(HttpStatusCode code)
    {
        var target = Target();

        var result = await Sender(_ => Status(code)).SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Succeeded, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Throttling_and_server_errors_are_retried(HttpStatusCode code)
    {
        var target = Target();

        var result = await Sender(_ => Status(code)).SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Retry, result.Outcome);
        Assert.Equal((int)code, result.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    public async Task Other_client_errors_and_redirects_are_final_failures(HttpStatusCode code)
    {
        var target = Target();

        var result = await Sender(_ => Status(code)).SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Failed, result.Outcome);
        Assert.False(result.EndpointGone);
    }

    [Fact]
    public async Task Gone_is_a_final_failure_that_flags_the_endpoint()
    {
        var target = Target();

        var result = await Sender(_ => Status(HttpStatusCode.Gone)).SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Failed, result.Outcome);
        Assert.True(result.EndpointGone);
    }

    [Fact]
    public async Task Retry_after_seconds_is_reported_and_capped()
    {
        var target = Target();
        var small = Sender(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return Task.FromResult(response);
        });
        var huge = Sender(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(3));
            return Task.FromResult(response);
        });

        var smallResult = await small.SendAsync(target, Message(target), Payload, CancellationToken.None);
        var hugeResult = await huge.SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(90), smallResult.RetryAfter);
        Assert.Equal(WebhookSender.MaxRetryAfter, hugeResult.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_http_date_is_converted_to_a_delay()
    {
        var target = Target();
        var sender = Sender(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddMinutes(5));
            return Task.FromResult(response);
        });

        var result = await sender.SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(5), result.RetryAfter);
    }

    [Fact]
    public async Task A_slow_subscriber_times_out_and_is_retried()
    {
        var target = Target();
        var sender = new WebhookSender(
            new HttpClient(new CancellableStubHandler()),
            _time,
            Options.Create(new SenderOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) }));

        var result = await sender.SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Retry, result.Outcome);
        Assert.Equal("timeout", result.Error);
    }

    [Fact]
    public async Task Shutdown_is_not_reported_as_a_timeout()
    {
        var target = Target();
        using var cts = new CancellationTokenSource();
        var sender = new WebhookSender(new HttpClient(new CancellableStubHandler()), _time, Options.Create(new SenderOptions()));
        var task = sender.SendAsync(target, Message(target), Payload, cts.Token).AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Connection_errors_are_retried()
    {
        var target = Target();

        var result = await Sender(_ => throw new HttpRequestException("connection refused"))
            .SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Retry, result.Outcome);
        Assert.Equal("connection_error", result.Error);
    }

    [Fact]
    public async Task A_blocked_address_is_a_final_failure()
    {
        var target = Target();

        var result = await Sender(_ => throw new HttpRequestException("blocked", new BlockedAddressException("blocked")))
            .SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Failed, result.Outcome);
        Assert.Equal("blocked_address", result.Error);
    }

    [Theory]
    [InlineData("http://subscriber.example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://localhost/hook")]
    public async Task Unsafe_urls_are_never_called(string url)
    {
        var called = false;
        var target = Target(url);

        var result = await Sender(_ =>
        {
            called = true;
            return Status(HttpStatusCode.OK);
        }).SendAsync(target, Message(target), Payload, CancellationToken.None);

        Assert.False(called);
        Assert.Equal(SendOutcome.Failed, result.Outcome);
        Assert.Equal("url_not_allowed", result.Error);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class CancellableStubHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
