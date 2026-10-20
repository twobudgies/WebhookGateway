namespace WebhookGateway.Ingest.Limits;

/// <summary>Token bucket: capacity tokens, refilled continuously at <c>ratePerSecond</c>. Thread-safe.</summary>
public sealed class TokenBucket
{
    private readonly Lock _gate = new();
    private readonly double _capacity;
    private readonly double _ratePerSecond;
    private double _tokens;
    private long _lastTimestamp;

    public TokenBucket(double capacity, double ratePerSecond, long nowTimestamp)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerSecond);
        _capacity = capacity;
        _ratePerSecond = ratePerSecond;
        _tokens = capacity;
        _lastTimestamp = nowTimestamp;
    }

    /// <summary>Takes one token if available.</summary>
    /// <returns>Whether it was taken, tokens left, and how long until one token is available when it was not.</returns>
    public (bool Taken, int Remaining, TimeSpan RetryAfter) TryTake(long nowTimestamp, long timestampFrequency)
    {
        lock (_gate)
        {
            var elapsedSeconds = (nowTimestamp - _lastTimestamp) / (double)timestampFrequency;
            if (elapsedSeconds > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + (elapsedSeconds * _ratePerSecond));
                _lastTimestamp = nowTimestamp;
            }

            if (_tokens >= 1)
            {
                _tokens -= 1;
                return (true, (int)_tokens, TimeSpan.Zero);
            }

            var wait = (1 - _tokens) / _ratePerSecond;
            return (false, 0, TimeSpan.FromSeconds(wait));
        }
    }
}
