using WebhookGateway.Dispatcher.Outbound;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class EndpointUrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com/hooks")]
    [InlineData("https://example.com:8443/hooks?x=1")]
    [InlineData("https://8.8.8.8/hook")]
    public void Accepts_public_https_urls(string url)
    {
        Assert.Null(EndpointUrlValidator.Validate(new Uri(url)));
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://api.localhost/hook")]
    [InlineData("https://db.internal/hook")]
    [InlineData("https://printer.local/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[fd00::1]/hook")]
    public void Rejects_unsafe_urls(string url)
    {
        Assert.NotNull(EndpointUrlValidator.Validate(new Uri(url)));
    }

    [Fact]
    public void Plain_http_is_allowed_only_when_explicitly_enabled()
    {
        var url = new Uri("http://example.com/hook");

        Assert.NotNull(EndpointUrlValidator.Validate(url));
        Assert.Null(EndpointUrlValidator.Validate(url, allowInsecureHttp: true));
    }

    [Fact]
    public void Relative_urls_are_rejected()
    {
        Assert.NotNull(EndpointUrlValidator.Validate(new Uri("/hook", UriKind.Relative)));
    }

    [Theory]
    [InlineData("http://receiver:8080/webhook")]
    [InlineData("http://localhost:9000/hook")]
    [InlineData("https://service.internal/hook")]
    [InlineData("http://10.0.0.7/hook")]
    public void Private_installs_may_target_internal_hosts(string url)
    {
        Assert.Null(EndpointUrlValidator.Validate(new Uri(url), allowInsecureHttp: true, allowPrivateNetworks: true));
    }

    [Fact]
    public void Private_installs_still_cannot_target_the_metadata_address()
    {
        Assert.NotNull(EndpointUrlValidator.Validate(new Uri("http://169.254.169.254/latest"), allowInsecureHttp: true, allowPrivateNetworks: true));
    }

    [Theory]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
    [InlineData("http://METADATA.GOOGLE.INTERNAL./x")]
    [InlineData("http://instance-data/latest/meta-data/")]
    [InlineData("http://[fd00:ec2::254]/latest/meta-data/")]
    public void Cloud_metadata_targets_are_refused_even_on_private_installs(string url)
    {
        Assert.NotNull(EndpointUrlValidator.Validate(new Uri(url), allowInsecureHttp: true, allowPrivateNetworks: true));
        Assert.NotNull(EndpointUrlValidator.Validate(new Uri(url), allowInsecureHttp: true, allowPrivateNetworks: false));
    }
}
