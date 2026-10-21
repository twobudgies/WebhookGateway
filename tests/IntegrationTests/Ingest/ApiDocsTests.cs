using System.Net;
using System.Text.Json;

namespace WebhookGateway.IntegrationTests.Ingest;

public sealed class ApiDocsTests : IClassFixture<IngestFactory>
{
    private readonly IngestFactory _factory;

    public ApiDocsTests(IngestFactory factory) => _factory = factory;

    [Fact]
    public async Task The_openapi_document_describes_the_events_api_with_its_security_scheme_and_idempotency_header()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = doc.RootElement.GetProperty("paths").GetProperty("/v1/events").GetProperty("post");
        Assert.Equal("Send an event", operation.GetProperty("summary").GetString());
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
        foreach (var status in new[] { "202", "400", "401", "403", "413", "429", "503" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"missing response {status}");
        }

        Assert.Equal("bearer", doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("scheme").GetString());
        Assert.False(doc.RootElement.GetProperty("paths").TryGetProperty("/healthz", out var health) && health.GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task The_swagger_page_is_served_at_docs()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/docs/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_docs_can_be_switched_off()
    {
        using var off = _factory.WithWebHostBuilder(b => b.UseSetting("ApiDocs:Enabled", "false"));
        using var client = off.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
