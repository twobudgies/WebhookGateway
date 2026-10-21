using System.Net;
using WebhookGateway.Dispatcher.Outbound;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class IpAddressPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("192.0.0.5")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:10.0.0.1")]    // IPv4-mapped private
    [InlineData("::ffff:169.254.169.254")]
    public void Blocks_non_public_addresses(string text)
    {
        Assert.True(IpAddressPolicy.IsBlocked(IPAddress.Parse(text)), text);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001::200e")]
    [InlineData("::ffff:8.8.8.8")]
    public void Allows_public_addresses(string text)
    {
        Assert.False(IpAddressPolicy.IsBlocked(IPAddress.Parse(text)), text);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.18.0.4")]
    [InlineData("192.168.1.10")]
    [InlineData("fd00::1")]
    public void The_private_install_policy_allows_private_and_loopback_addresses(string text)
    {
        Assert.False(IpAddressPolicy.IsBlockedAllowingPrivate(IPAddress.Parse(text)), text);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("fd00:ec2::254")]
    public void The_private_install_policy_still_blocks_metadata_unspecified_and_multicast(string text)
    {
        Assert.True(IpAddressPolicy.IsBlockedAllowingPrivate(IPAddress.Parse(text)), text);
    }
}
