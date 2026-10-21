using StackExchange.Redis;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Valkey;

/// <summary>
/// Shared token bucket per tenant, one atomic Lua script (docs/tenancy-and-limits.md section 3). The server clock is
/// used so tasks with different clocks agree. Throws when Valkey is unreachable; Ingest then falls back to its local limiter.
/// </summary>
public sealed class ValkeyRateLimiter(IConnectionMultiplexer valkey) : IRateLimiter
{
    // KEYS[1] bucket; ARGV: capacity, refill per second. Returns {allowed, tokensLeft, retryAfterMs}.
    private const string Script = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local capacity = tonumber(ARGV[1])
        local rate = tonumber(ARGV[2])
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(state[1])
        local ts = tonumber(state[2])
        if tokens == nil then
          tokens = capacity
          ts = now
        end
        tokens = math.min(capacity, tokens + math.max(0, now - ts) * rate / 1000)
        local allowed = 0
        local retry = 0
        if tokens >= 1 then
          tokens = tokens - 1
          allowed = 1
        else
          retry = math.ceil((1 - tokens) * 1000 / rate)
        end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
        redis.call('PEXPIRE', KEYS[1], math.ceil(capacity / rate * 1000) + 60000)
        return {allowed, math.floor(tokens), retry}
        """;

    public async ValueTask<RateLimitResult> TryAcquireAsync(TenantId tenantId, PlanLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var result = (long[]?)await valkey.GetDatabase().ScriptEvaluateAsync(
            Script,
            [(RedisKey)$"rl:{tenantId.Value}"],
            [limits.BurstCapacity, limits.EventsPerSecond]).ConfigureAwait(false);
        return new RateLimitResult(
            result![0] == 1,
            limits.EventsPerSecond,
            (int)result[1],
            TimeSpan.FromMilliseconds(result[2]));
    }
}
