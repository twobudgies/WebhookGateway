using System.Net;
using System.Net.Sockets;
using System.Text;
using WebhookGateway.Dispatcher.Outbound;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class SafeHttpHandlerTests
{
    [Fact]
    public async Task Loopback_is_refused_at_connect_time_by_default()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new HttpClient(SafeHttpHandler.Create(TimeSpan.FromSeconds(2), 10));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri($"http://127.0.0.1:{port}/")));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }

    [Fact]
    public async Task A_hostname_that_resolves_to_loopback_is_refused()
    {
        using var client = new HttpClient(SafeHttpHandler.Create(TimeSpan.FromSeconds(2), 10));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://localhost:9/")));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }

    [Fact]
    public async Task When_the_policy_allows_the_address_the_request_goes_through_and_redirects_are_not_followed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            var buffer = new byte[4096];
            _ = await socket.ReceiveAsync(buffer);
            var response = "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await socket.SendAsync(Encoding.ASCII.GetBytes(response));
        });
        using var client = new HttpClient(SafeHttpHandler.Create(TimeSpan.FromSeconds(2), 10, isBlocked: _ => false));

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"));
        await server;

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
