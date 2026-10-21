# Verify a webhook from WebhookGateway (Standard Webhooks). Use the RAW request body.
require "base64"
require "openssl"

def verify(secret, webhook_id, timestamp, signature_header, body, tolerance_seconds = 300)
  return false if (Time.now.to_i - timestamp.to_i).abs > tolerance_seconds # stops replays

  key = Base64.strict_decode64(secret.delete_prefix("whsec_"))
  expected = Base64.strict_encode64(OpenSSL::HMAC.digest("SHA256", key, "#{webhook_id}.#{timestamp}.#{body}"))
  # The header can hold several signatures ("v1,abc v1,def") while a secret is being rotated.
  signature_header.split.any? { |part| part.start_with?("v1,") && secure_compare(expected, part[3..]) }
end

# Constant-time comparison, so response timing does not leak how many characters matched.
def secure_compare(a, b)
  return false unless a.bytesize == b.bytesize

  a.bytes.zip(b.bytes).reduce(0) { |acc, (x, y)| acc | (x ^ y) }.zero?
end

if $PROGRAM_NAME == __FILE__ # used by samples/test.sh
  ok = verify(ENV["SECRET"], ENV["WEBHOOK_ID"], ENV["TIMESTAMP"], ENV["SIGNATURE"], ENV["BODY"], ENV.fetch("TOLERANCE_SECONDS", "300").to_i)
  puts ok ? "VALID" : "INVALID"
end
