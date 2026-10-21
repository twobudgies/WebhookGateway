# Ruby 2.6+: `ruby send_event.rb`. Standard library only. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local).
require "json"
require "net/http"
require "securerandom"

URL = ENV.fetch("WEBHOOKGATEWAY_URL", "http://localhost:8080")
API_KEY = ENV.fetch("WEBHOOKGATEWAY_API_KEY")

def send_event(type, data, retries: 4)
  idempotency_key = SecureRandom.uuid # one key for all retries of the same event
  uri = URI("#{URL}/v1/events")
  (0..retries).each do |attempt|
    request = Net::HTTP::Post.new(uri, "Authorization" => "Bearer #{API_KEY}", "Content-Type" => "application/json", "Idempotency-Key" => idempotency_key)
    request.body = { type: type, data: data }.to_json
    response = Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https", read_timeout: 10) { |http| http.request(request) }
    if %w[429 503].include?(response.code) && attempt < retries
      sleep((response["Retry-After"] || 2**attempt).to_f)
      next
    end
    return [response.code.to_i, response.body]
  end
end

p send_event("order.created", { orderId: 42, amount: 1999 }) if $PROGRAM_NAME == __FILE__
