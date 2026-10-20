using System.Net;

namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>Static checks on a subscriber URL, done when the endpoint is saved and again before each send.</summary>
public static class EndpointUrlValidator
{
    /// <returns>Null when the URL is acceptable, otherwise the reason.</returns>
    public static string? Validate(Uri url, bool allowInsecureHttp = false)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsAbsoluteUri)
        {
            return "URL must be absolute.";
        }

        if (url.Scheme != Uri.UriSchemeHttps && !(allowInsecureHttp && url.Scheme == Uri.UriSchemeHttp))
        {
            return "URL must use https.";
        }

        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            return "URL must not contain credentials.";
        }

        if (url.IsDefaultPort is false && url.Port is <= 0 or > 65535)
        {
            return "URL port is invalid.";
        }

        var host = url.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return "Host name is not allowed.";
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal) && IpAddressPolicy.IsBlocked(literal))
        {
            return "Address is not allowed.";
        }

        return null;
    }
}
