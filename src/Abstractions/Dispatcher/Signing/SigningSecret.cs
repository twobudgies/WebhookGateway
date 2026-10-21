using System.Security.Cryptography;

namespace WebhookGateway.Dispatcher.Signing;

/// <summary>
/// HMAC key for one endpoint, in the Standard Webhooks text form <c>whsec_&lt;base64&gt;</c>.
/// An endpoint can have two active secrets during rotation.
/// </summary>
public sealed class SigningSecret
{
    public const string Prefix = "whsec_";
    public const int GeneratedKeyBytes = 32;

    private readonly byte[] _key;

    private SigningSecret(byte[] key) => _key = key;

    public static SigningSecret Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"A signing secret must start with '{Prefix}'.");
        }

        try
        {
            var key = Convert.FromBase64String(text[Prefix.Length..]);
            return key.Length == 0 ? throw new FormatException("A signing secret must not be empty.") : new SigningSecret(key);
        }
        catch (FormatException ex) when (ex.Message.Contains("base-64", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("A signing secret must be valid base64 after the prefix.", ex);
        }
    }

    public static SigningSecret Generate() => new(RandomNumberGenerator.GetBytes(GeneratedKeyBytes));

    public string ToText() => Prefix + Convert.ToBase64String(_key);

    public byte[] Sign(ReadOnlySpan<byte> data) => HMACSHA256.HashData(_key, data);
}
