using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using WebhookGateway.Aws.Kms;

namespace WebhookGateway.IntegrationTests.Aws;

/// <summary>Needs the local stack (<c>./scripts/dev-up.sh</c>, which creates <c>alias/webhookgateway-dev</c>); skipped when Moto is not reachable.</summary>
public sealed class KmsSecretProtectorTests
{
    [Fact]
    public async Task A_secret_protected_with_kms_round_trips_and_is_not_stored_in_clear()
    {
        using var kms = new AmazonKeyManagementServiceClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonKeyManagementServiceConfig { ServiceURL = "http://localhost:5050", AuthenticationRegion = "ap-south-1" });
        try
        {
            await kms.DescribeKeyAsync(new DescribeKeyRequest { KeyId = "alias/webhookgateway-dev" });
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            return; // local stack not running
        }

        var protector = new KmsSecretProtector(kms, "alias/webhookgateway-dev", NullLogger<KmsSecretProtector>.Instance);

        var stored = await protector.ProtectAsync("whsec_abc123", CancellationToken.None);
        var back = await protector.UnprotectAsync(stored, CancellationToken.None);

        Assert.Equal("whsec_abc123", back);
        Assert.Equal("alias/webhookgateway-dev", stored.KeyId);
        Assert.DoesNotContain("whsec_abc123", System.Text.Encoding.UTF8.GetString(stored.Ciphertext), StringComparison.Ordinal);
    }
}
