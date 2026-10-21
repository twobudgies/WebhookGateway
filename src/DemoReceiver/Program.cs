// A tiny webhook receiver for trying WebhookGateway. It does what every real receiver should do:
// 1. read the RAW body, 2. verify the signature (Standard Webhooks) and the timestamp, 3. answer 2xx quickly.
// It keeps the last events in memory and shows them at GET / (page) and GET /received (JSON).
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var received = new ConcurrentQueue<object>();
string? secret = Environment.GetEnvironmentVariable("RECEIVER_SECRET");

app.MapPost("/webhook", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();
    var id = request.Headers["webhook-id"].ToString();
    var timestamp = request.Headers["webhook-timestamp"].ToString();
    var signatures = request.Headers["webhook-signature"].ToString();

    string verified;
    if (string.IsNullOrEmpty(secret))
    {
        verified = "not checked (no secret configured yet)";
    }
    else if (!IsValid(secret, id, timestamp, signatures, body))
    {
        app.Logger.LogWarning("Rejected webhook {WebhookId}: bad signature or timestamp", id);
        return Results.Json(new { error = "invalid signature" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    else
    {
        verified = "valid";
    }

    received.Enqueue(new { receivedAt = DateTimeOffset.UtcNow, webhookId = id, signature = verified, body = JsonSerializer.Deserialize<JsonElement>(body) });
    while (received.Count > 100)
    {
        received.TryDequeue(out _);
    }

    app.Logger.LogInformation("Received webhook {WebhookId}, signature {Signature}", id, verified);
    return Results.Ok();
});

// Demo only: lets the quickstart script hand over the endpoint's signing secret.
app.MapPut("/secret", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    secret = (await reader.ReadToEndAsync()).Trim();
    return Results.NoContent();
});

app.MapGet("/received", () => Results.Json(received.Reverse()));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/", () => Results.Content(
    """
    <!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="3"><title>Demo receiver</title>
    <body style="font-family:system-ui;max-width:48rem;margin:2rem auto;padding:0 1rem">
    <h1>Demo receiver</h1><p>Events delivered by WebhookGateway appear here (refreshes every 3 s). Raw JSON: <a href="/received">/received</a>.</p>
    <pre id="e" style="background:#f4f4f4;padding:1rem;overflow:auto">loading...</pre>
    <script>fetch('/received').then(r=>r.json()).then(j=>e.textContent=j.length?JSON.stringify(j,null,2):'nothing yet - send an event')</script>
    """,
    "text/html"));

app.Run();

// Standard Webhooks: signature = base64(HMAC-SHA256(key, "{id}.{timestamp}.{body}")), header "v1,<sig> v1,<sig2>" (several during rotation).
static bool IsValid(string secret, string id, string timestamp, string signatureHeader, string body)
{
    if (!long.TryParse(timestamp, out var seconds)
        || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300)
    {
        return false; // too old or from the future: protects against replays
    }

    var key = Convert.FromBase64String(secret["whsec_".Length..]);
    var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
    foreach (var candidate in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        if (candidate.StartsWith("v1,", StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(candidate[3..])))
        {
            return true;
        }
    }

    return false;
}
