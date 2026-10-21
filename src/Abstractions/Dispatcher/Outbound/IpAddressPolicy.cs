using System.Net;
using System.Net.Sockets;

namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>
/// Decides which destination addresses a subscriber URL may resolve to. Everything that is not a public unicast
/// address is blocked: loopback, private, link-local (including the cloud metadata address), CGNAT, multicast,
/// reserved and unspecified ranges, for IPv4 and IPv6 (including IPv4-mapped IPv6).
/// </summary>
public static class IpAddressPolicy
{
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsBlockedV6(address),
            _ => true,
        };
    }

    /// <summary>
    /// The relaxed policy for private installs (<c>AllowPrivateNetworks</c>): loopback, RFC 1918, CGNAT and unique-local
    /// addresses are allowed because the operator delivers to their own services, but the cloud metadata/link-local range,
    /// unspecified, multicast and reserved ranges stay blocked.
    /// </summary>
    public static bool IsBlockedAllowingPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0 || (b[0] == 169 && b[1] == 254) || b[0] >= 224;
        }

        return address.AddressFamily != AddressFamily.InterNetworkV6
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.Equals(AwsMetadataIPv6);
    }

    /// <summary>The AWS instance metadata service on IPv6. It sits in the unique-local range that private installs allow, so it is listed explicitly.</summary>
    private static readonly IPAddress AwsMetadataIPv6 = IPAddress.Parse("fd00:ec2::254");

    /// <summary>Host names of cloud metadata services. They are refused when an endpoint is saved, in every mode.</summary>
    public static bool IsMetadataHostName(string host) =>
        MetadataHostNames.Contains(host.TrimEnd('.'));

    private static readonly HashSet<string> MetadataHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "metadata",
        "metadata.google.internal",
        "metadata.goog",
        "instance-data",
        "instance-data.ec2.internal",
    };

    private static bool IsBlockedV4(byte[] b)
    {
        var (a, c) = (b[0], b[1]);
        return a == 0                                   // "this" network, unspecified
            || a == 10                                  // private
            || a == 127                                 // loopback
            || (a == 100 && c is >= 64 and <= 127)      // CGNAT 100.64.0.0/10
            || (a == 169 && c == 254)                   // link-local, cloud metadata
            || (a == 172 && c is >= 16 and <= 31)       // private
            || (a == 192 && c == 0 && b[2] == 0)        // IETF protocol assignments 192.0.0.0/24
            || (a == 192 && c == 0 && b[2] == 2)        // documentation 192.0.2.0/24
            || (a == 192 && c == 168)                   // private
            || (a == 198 && c is 18 or 19)              // benchmarking 198.18.0.0/15
            || (a == 198 && c == 51 && b[2] == 100)     // documentation 198.51.100.0/24
            || (a == 203 && c == 0 && b[2] == 113)      // documentation 203.0.113.0/24
            || a >= 224;                                // multicast, reserved, broadcast
    }

    private static bool IsBlockedV6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        var b = address.GetAddressBytes();
        return (b[0] & 0xFE) == 0xFC                                  // unique local fc00::/7
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)  // documentation 2001:db8::/32
            || (b[0] == 0x01 && b[1] == 0x00 && b[2] == 0 && b[3] == 0 && b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0) // discard 100::/64
            || !(b[0] is >= 0x20 and <= 0x3F);                        // only global unicast 2000::/3 is allowed
    }
}
