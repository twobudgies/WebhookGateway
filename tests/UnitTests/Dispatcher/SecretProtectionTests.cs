using System.Security.Cryptography;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class SecretProtectionTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task A_secret_round_trips_and_the_stored_bytes_do_not_contain_it()
    {
        var protector = new LocalSecretProtector(Key);
        var secret = SigningSecret.Generate().ToText();

        var stored = await protector.ProtectAsync(secret, CancellationToken.None);
        var back = await protector.UnprotectAsync(stored, CancellationToken.None);

        Assert.Equal(secret, back);
        Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(stored.Ciphertext), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_secret_encrypts_differently_each_time()
    {
        var protector = new LocalSecretProtector(Key);

        var a = await protector.ProtectAsync("whsec_x", CancellationToken.None);
        var b = await protector.ProtectAsync("whsec_x", CancellationToken.None);

        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    [Fact]
    public async Task A_tampered_or_foreign_secret_is_rejected()
    {
        var protector = new LocalSecretProtector(Key);
        var stored = await protector.ProtectAsync("whsec_x", CancellationToken.None);
        stored.Ciphertext[^1] ^= 1;

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await protector.UnprotectAsync(stored, CancellationToken.None));
        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await protector.UnprotectAsync(new ProtectedSecret(stored.Ciphertext, "arn:aws:kms:other"), CancellationToken.None));
    }
}
