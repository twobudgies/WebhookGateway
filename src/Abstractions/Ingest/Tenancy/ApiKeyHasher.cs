using System.Security.Cryptography;
using System.Text;

namespace WebhookGateway.Ingest.Tenancy;

/// <summary>API keys are never stored: only their SHA-256 (lowercase hex) is.</summary>
public static class ApiKeyHasher
{
    public static string Hash(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
    }
}
