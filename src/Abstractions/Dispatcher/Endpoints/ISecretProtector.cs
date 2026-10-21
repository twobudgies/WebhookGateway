namespace WebhookGateway.Dispatcher.Endpoints;

/// <param name="Ciphertext">Everything needed to decrypt except the master key (data key blob, nonce, tag, ciphertext).</param>
/// <param name="KeyId">Which master key protected it (KMS key id/alias, or <c>local</c> in development).</param>
public sealed record ProtectedSecret(byte[] Ciphertext, string KeyId);

/// <summary>Envelope encryption of endpoint signing secrets (D26): only ciphertext is ever stored in the database.</summary>
public interface ISecretProtector
{
    ValueTask<ProtectedSecret> ProtectAsync(string plaintext, CancellationToken cancellationToken);

    ValueTask<string> UnprotectAsync(ProtectedSecret secret, CancellationToken cancellationToken);
}
