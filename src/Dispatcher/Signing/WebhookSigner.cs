using System.Globalization;
using System.Text;

namespace WebhookGateway.Dispatcher.Signing;

/// <summary>Standard Webhooks signatures: <c>v1,base64(HMAC-SHA256(secret, "{id}.{timestamp}.{body}"))</c>.</summary>
public static class WebhookSigner
{
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    /// <returns>One signature per secret, space separated (the form receivers accept during rotation).</returns>
    public static string Sign(string webhookId, long timestampSeconds, ReadOnlySpan<byte> body, IReadOnlyList<SigningSecret> secrets)
    {
        ArgumentNullException.ThrowIfNull(webhookId);
        ArgumentNullException.ThrowIfNull(secrets);
        if (secrets.Count == 0)
        {
            throw new ArgumentException("At least one signing secret is required.", nameof(secrets));
        }

        var prefix = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{webhookId}.{timestampSeconds}."));
        var signedContent = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signedContent, 0);
        body.CopyTo(signedContent.AsSpan(prefix.Length));

        return string.Join(' ', secrets.Select(s => "v1," + Convert.ToBase64String(s.Sign(signedContent))));
    }
}
