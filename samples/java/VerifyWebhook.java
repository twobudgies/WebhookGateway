// Java 11+: `java VerifyWebhook.java`. Verifies a webhook (Standard Webhooks) from environment variables, for the sample test.
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public class VerifyWebhook {
    static boolean verify(String secret, String webhookId, String timestamp, String signatureHeader, String body, long toleranceSeconds) throws Exception {
        // Reject old or future timestamps: stops replays.
        if (Math.abs(System.currentTimeMillis() / 1000 - Long.parseLong(timestamp)) > toleranceSeconds) {
            return false;
        }
        byte[] key = Base64.getDecoder().decode(secret.substring("whsec_".length()));
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(key, "HmacSHA256"));
        byte[] expected = mac.doFinal((webhookId + "." + timestamp + "." + body).getBytes(StandardCharsets.UTF_8));
        // The header can hold several signatures ("v1,abc v1,def") while a secret is being rotated.
        for (String part : signatureHeader.trim().split("\\s+")) {
            if (part.startsWith("v1,") && MessageDigest.isEqual(expected, Base64.getDecoder().decode(part.substring(3)))) {
                return true;
            }
        }
        return false;
    }

    public static void main(String[] args) throws Exception {
        long tolerance = Long.parseLong(System.getenv().getOrDefault("TOLERANCE_SECONDS", "300"));
        boolean ok = verify(System.getenv("SECRET"), System.getenv("WEBHOOK_ID"), System.getenv("TIMESTAMP"),
                System.getenv("SIGNATURE"), System.getenv("BODY"), tolerance);
        System.out.println(ok ? "VALID" : "INVALID");
    }
}
