using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Logging;
using WebhookGateway.Dispatcher.Endpoints;

namespace WebhookGateway.Aws.Kms;

/// <summary>
/// Envelope encryption with KMS (D26): a fresh data key per secret from <c>GenerateDataKey</c>, the secret encrypted with
/// AES-GCM, and only the KMS-encrypted data key stored next to the ciphertext. Layout: <c>[keyBlobLength 2][keyBlob][AES-GCM envelope]</c>.
/// Decrypting costs one KMS call, so readers cache decrypted endpoints.
/// </summary>
public sealed partial class KmsSecretProtector(IAmazonKeyManagementService kms, string keyId, ILogger<KmsSecretProtector> logger) : ISecretProtector
{
    public async ValueTask<ProtectedSecret> ProtectAsync(string plaintext, CancellationToken cancellationToken)
    {
        var dataKey = await kms.GenerateDataKeyAsync(
            new GenerateDataKeyRequest { KeyId = keyId, KeySpec = DataKeySpec.AES_256 },
            cancellationToken).ConfigureAwait(false);
        var blob = dataKey.CiphertextBlob.ToArray();
        var envelope = AesGcmEnvelope.Encrypt(dataKey.Plaintext.ToArray(), plaintext);
        var result = new byte[2 + blob.Length + envelope.Length];
        result[0] = (byte)(blob.Length >> 8);
        result[1] = (byte)blob.Length;
        blob.CopyTo(result, 2);
        envelope.CopyTo(result, 2 + blob.Length);
        LogProtected(logger, keyId);
        return new ProtectedSecret(result, keyId);
    }

    public async ValueTask<string> UnprotectAsync(ProtectedSecret secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var bytes = secret.Ciphertext;
        var blobLength = (bytes[0] << 8) | bytes[1];
        using var blob = new MemoryStream(bytes, 2, blobLength);
        var key = await kms.DecryptAsync(new DecryptRequest { CiphertextBlob = blob }, cancellationToken).ConfigureAwait(false);
        return AesGcmEnvelope.Decrypt(key.Plaintext.ToArray(), bytes.AsSpan(2 + blobLength));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Protected an endpoint secret with KMS key {KeyId}")]
    private static partial void LogProtected(ILogger logger, string keyId);
}
