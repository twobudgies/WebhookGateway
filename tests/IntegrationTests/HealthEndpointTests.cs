using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebhookGateway.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Ingest_healthz_returns_ok()
    {
        await using var factory = new WebApplicationFactory<WebhookGateway.Ingest.IngestApiMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ControlPlane_healthz_returns_ok()
    {
        await using var factory = new WebApplicationFactory<WebhookGateway.ControlPlane.ControlPlaneApiMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
