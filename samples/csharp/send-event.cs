// .NET 10: run with `dotnet run send-event.cs`. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local).
using System.Net;
using System.Net.Http.Headers;
using System.Text;

var url = Environment.GetEnvironmentVariable("WEBHOOKGATEWAY_URL") ?? "http://localhost:8080";
var apiKey = Environment.GetEnvironmentVariable("WEBHOOKGATEWAY_API_KEY")!;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

// One idempotency key for all retries of the same event, so a retry never creates a duplicate.
var idempotencyKey = Guid.NewGuid().ToString();
for (var attempt = 0; ; attempt++)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v1/events")
    {
        Content = new StringContent("""{"type":"order.created","data":{"orderId":42,"amount":1999}}""", Encoding.UTF8, "application/json"),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    request.Headers.Add("Idempotency-Key", idempotencyKey);
    using var response = await http.SendAsync(request);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < 4)
    {
        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(wait);
        continue;
    }

    Console.WriteLine($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    break;
}
