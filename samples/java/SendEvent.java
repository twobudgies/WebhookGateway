// Java 11+: `java SendEvent.java`. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local).
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.UUID;

public class SendEvent {
    public static void main(String[] args) throws Exception {
        String url = System.getenv().getOrDefault("WEBHOOKGATEWAY_URL", "http://localhost:8080");
        String apiKey = System.getenv("WEBHOOKGATEWAY_API_KEY");
        HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build();
        String idempotencyKey = UUID.randomUUID().toString(); // one key for all retries of the same event
        String body = "{\"type\":\"order.created\",\"data\":{\"orderId\":42,\"amount\":1999}}";

        for (int attempt = 0; ; attempt++) {
            HttpRequest request = HttpRequest.newBuilder(URI.create(url + "/v1/events"))
                    .timeout(Duration.ofSeconds(10))
                    .header("Authorization", "Bearer " + apiKey)
                    .header("Content-Type", "application/json")
                    .header("Idempotency-Key", idempotencyKey)
                    .POST(HttpRequest.BodyPublishers.ofString(body))
                    .build();
            HttpResponse<String> response = http.send(request, HttpResponse.BodyHandlers.ofString());
            if ((response.statusCode() == 429 || response.statusCode() == 503) && attempt < 4) {
                long seconds = response.headers().firstValueAsLong("Retry-After").orElse(1L << attempt);
                Thread.sleep(seconds * 1000);
                continue;
            }
            System.out.println(response.statusCode() + " " + response.body());
            break;
        }
    }
}
