using System.Security.Cryptography;

namespace WebhookGateway.Dispatcher.Endpoints;

/// <summary>
/// Development and test protector: one AES key from configuration. Production uses KMS (<c>KmsSecretProtector</c>);
/// this one must never be used there because the key sits next to the application.
/// </summary>
public sealed class LocalSecretProtector(byte[] key) : ISecretProtector
{
    public const string KeyId = "local";

    public ValueTask<ProtectedSecret> ProtectAsync(string plaintext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ProtectedSecret(AesGcmEnvelope.Encrypt(key, plaintext), KeyId));

    public ValueTask<string> UnprotectAsync(ProtectedSecret secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return secret.KeyId == KeyId
            ? ValueTask.FromResult(AesGcmEnvelope.Decrypt(key, secret.Ciphertext))
            : throw new CryptographicException($"Secret was protected with key '{secret.KeyId}', not the local key.");
    }

    public static LocalSecretProtector FromBase64(string base64Key) => new(Convert.FromBase64String(base64Key));
}
