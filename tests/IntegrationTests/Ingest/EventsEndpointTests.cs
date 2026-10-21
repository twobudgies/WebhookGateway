using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Ingest.Api;
using WebhookGateway.Ingest.Publishing;

namespace WebhookGateway.IntegrationTests.Ingest;

public sealed class EventsEndpointTests : IClassFixture<IngestFactory>
{
    private static readonly Uri EventsUri = new("/v1/events", UriKind.Relative);
    private const string ValidBody = """{"type":"order.created","occurredAt":"2026-10-20T09:59:00Z","data":{"orderId":42}}""";

    private readonly IngestFactory _factory;

    public EventsEndpointTests(IngestFactory factory) => _factory = factory;

    private HttpClient Client(string? apiKey = IngestFactory.ActiveKey)
    {
        var client = _factory.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string> ErrorCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Valid_event_is_accepted_and_published()
    {
        var before = _factory.Published.Messages.Count;
        using var client = Client();

        using var response = await client.PostAsync(EventsUri, Json(ValidBody));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var eventId = EventId.Parse(doc.RootElement.GetProperty("eventId").GetString()!);
        var message = Assert.Single(_factory.Published.Messages, m => m.EventId == eventId);
        Assert.True(_factory.Published.Messages.Count > before);
        Assert.Equal(_factory.ActiveTenant, message.TenantId);
        Assert.Equal("order.created", message.Type);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 9, 59, 0, TimeSpan.Zero), message.OccurredAt);
        Assert.Equal(42, message.Payload.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Idempotency_key_is_carried_on_the_message()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, EventsUri) { Content = Json(ValidBody) };
        request.Headers.Add("Idempotency-Key", "order-42-created");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(_factory.Published.Messages, m => m.IdempotencyKey == "order-42-created");
    }

    [Fact]
    public async Task Missing_authorization_is_rejected()
    {
        using var client = Client(apiKey: null);

        using var response = await client.PostAsync(EventsUri, Json(ValidBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCode(response));
    }

    [Fact]
    public async Task Unknown_key_is_rejected()
    {
        using var client = Client("wgk_not_a_real_key");

        using var response = await client.PostAsync(EventsUri, Json(ValidBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Suspended_tenant_is_forbidden()
    {
        using var client = Client(IngestFactory.SuspendedKey);

        using var response = await client.PostAsync(EventsUri, Json(ValidBody));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("tenant_suspended", await ErrorCode(response));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"type":"","data":{}}""")]
    [InlineData("""{"type":"has space","data":{}}""")]
    [InlineData("""{"type":"a.b"}""")]
    [InlineData("""{"type":"a.b","data":"text"}""")]
    [InlineData("""{"type":"a.b","occurredAt":"yesterday","data":{}}""")]
    public async Task Invalid_bodies_get_400(string body)
    {
        using var client = Client();

        using var response = await client.PostAsync(EventsUri, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ErrorCode(response));
    }

    [Fact]
    public async Task Overlong_idempotency_key_gets_400()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, EventsUri) { Content = Json(ValidBody) };
        request.Headers.Add("Idempotency-Key", new string('k', EventsEndpoint.MaxIdempotencyKeyLength + 1));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_body_gets_413()
    {
        using var client = Client();
        var blob = new string('x', 300 * 1024);
        var body = "{\"type\":\"a.b\",\"data\":{\"blob\":\"" + blob + "\"}}";

        using var response = await client.PostAsync(EventsUri, Json(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", await ErrorCode(response));
    }

    [Fact]
    public async Task Exceeding_the_burst_gets_429_with_headers_and_recovers_with_time()
    {
        using var factory = new IngestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IngestFactory.ActiveKey);

        // Basic: burst 200 at 100/s. Time is frozen, so nothing refills.
        for (var i = 0; i < 200; i++)
        {
            using var ok = await client.PostAsync(EventsUri, Json(ValidBody));
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var limited = await client.PostAsync(EventsUri, Json(ValidBody));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await ErrorCode(limited));
        Assert.Equal("100", limited.Headers.GetValues("RateLimit-Limit").Single());
        Assert.Equal("0", limited.Headers.GetValues("RateLimit-Remaining").Single());
        Assert.Equal("1", limited.Headers.GetValues("Retry-After").Single());

        factory.Time.Advance(TimeSpan.FromSeconds(1));

        using var recovered = await client.PostAsync(EventsUri, Json(ValidBody));
        Assert.Equal(HttpStatusCode.Accepted, recovered.StatusCode);
    }
}

public sealed class EventsEndpointPublishFailureTests : IClassFixture<IngestFactory>
{
    private readonly IngestFactory _factory;

    public EventsEndpointPublishFailureTests(IngestFactory factory) => _factory = factory;

    [Fact]
    public async Task A_failing_queue_gives_503_and_not_an_accepted_event()
    {
        using var failing = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IEventPublisher, ThrowingPublisher>()));
        using var client = failing.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IngestFactory.ActiveKey);
        using var content = new StringContent("""{"type":"order.created","data":{"a":1}}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/v1/events", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class ThrowingPublisher : IEventPublisher
    {
        public ValueTask PublishAsync(EventMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("queue down");
    }
}
