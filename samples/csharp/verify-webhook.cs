// .NET 10: `dotnet run verify-webhook.cs`. Verifies a webhook (Standard Webhooks) from environment variables, for the sample test.
using System.Security.Cryptography;
using System.Text;

static bool Verify(string secret, string webhookId, string timestamp, string signatureHeader, string body, int toleranceSeconds = 300)
{
    // Reject old or future timestamps: stops replays.
    if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(timestamp)) > toleranceSeconds)
    {
        return false;
    }

    var key = Convert.FromBase64String(secret["whsec_".Length..]);
    var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{webhookId}.{timestamp}.{body}"));
    // The header can hold several signatures ("v1,abc v1,def") while a secret is being rotated.
    return signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(part => part.StartsWith("v1,", StringComparison.Ordinal))
        .Any(part => CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(part[3..])));
}

static string Env(string name) => Environment.GetEnvironmentVariable(name)!;

var ok = Verify(Env("SECRET"), Env("WEBHOOK_ID"), Env("TIMESTAMP"), Env("SIGNATURE"), Env("BODY"),
    int.Parse(Environment.GetEnvironmentVariable("TOLERANCE_SECONDS") ?? "300"));
Console.WriteLine(ok ? "VALID" : "INVALID");
