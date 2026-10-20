using System.Net;
using System.Net.Sockets;

namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>
/// Creates the <see cref="SocketsHttpHandler"/> used for all subscriber calls. The destination address is validated
/// at connect time on the exact address that is dialed, which closes the DNS-rebinding gap that a check before the
/// request would leave. Redirects are never followed.
/// </summary>
public static class SafeHttpHandler
{
    public static SocketsHttpHandler Create(TimeSpan connectTimeout, int maxConnectionsPerServer, Func<IPAddress, bool>? isBlocked = null)
    {
        var blocked = isBlocked ?? IpAddressPolicy.IsBlocked;
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = connectTimeout,
            MaxConnectionsPerServer = maxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = (context, cancellationToken) => ConnectAsync(context, blocked, cancellationToken),
        };
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        Func<IPAddress, bool> isBlocked,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        var allowed = addresses.Where(a => !isBlocked(a)).ToArray();
        if (allowed.Length == 0)
        {
            throw new BlockedAddressException($"'{host}' resolves only to addresses that are not allowed.");
        }

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw new HttpRequestException("Could not connect to the subscriber.", last);
    }
}
