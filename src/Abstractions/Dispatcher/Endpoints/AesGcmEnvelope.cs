using System.Security.Cryptography;
using System.Text;

namespace WebhookGateway.Dispatcher.Endpoints;

/// <summary>
/// AES-256-GCM with a random nonce. Layout: <c>[nonce 12][tag 16][ciphertext]</c>. Shared by the KMS and local protectors,
/// which differ only in where the 256-bit key comes from.
/// </summary>
public static class AesGcmEnvelope
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] Encrypt(byte[] key, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + data.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize));
        return result;
    }

    public static string Decrypt(byte[] key, ReadOnlySpan<byte> envelope)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (envelope.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted secret is too short.");
        }

        var data = new byte[envelope.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(envelope[..NonceSize], envelope[(NonceSize + TagSize)..], envelope.Slice(NonceSize, TagSize), data);
        return Encoding.UTF8.GetString(data);
    }
}
