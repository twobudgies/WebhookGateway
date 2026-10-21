using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.Ingest.Limits;

/// <summary>
/// If the shared store (Valkey) is unreachable, Ingest keeps accepting events: the rate limit falls back to the local
/// limiter, idempotency and quota checks are skipped (docs/architecture.md section 5, degraded mode). Each failure is logged.
/// </summary>
public sealed partial class ResilientRateLimiter(IRateLimiter shared, IRateLimiter local, ILogger<ResilientRateLimiter> logger) : IRateLimiter
{
    public async ValueTask<RateLimitResult> TryAcquireAsync(TenantId tenantId, PlanLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            return await shared.TryAcquireAsync(tenantId, limits, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallback(logger, ex, tenantId.Value);
            return await local.TryAcquireAsync(tenantId, limits, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared rate limiter unavailable; using the local limiter for tenant {TenantId}")]
    private static partial void LogFallback(ILogger logger, Exception exception, string tenantId);
}

public sealed partial class ResilientIdempotencyStore(IIdempotencyStore shared, ILogger<ResilientIdempotencyStore> logger) : IIdempotencyStore
{
    public async ValueTask<IdempotencyClaim> TryClaimAsync(TenantId tenantId, string key, EventId eventId, TimeSpan window, CancellationToken cancellationToken)
    {
        try
        {
            return await shared.TryClaimAsync(tenantId, key, eventId, window, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSkipped(logger, ex, tenantId.Value);
            return new IdempotencyClaim(true, null);
        }
    }

    public async ValueTask ReleaseAsync(TenantId tenantId, string key, CancellationToken cancellationToken)
    {
        try
        {
            await shared.ReleaseAsync(tenantId, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSkipped(logger, ex, tenantId.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency store unavailable; the idempotency check is skipped for tenant {TenantId}")]
    private static partial void LogSkipped(ILogger logger, Exception exception, string tenantId);
}

public sealed partial class ResilientQuotaCounter(IQuotaCounter shared, ILogger<ResilientQuotaCounter> logger) : IQuotaCounter
{
    public async ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await shared.IncrementAsync(tenantId, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSkipped(logger, ex, tenantId.Value);
            return 0;
        }
    }

    public async ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await shared.DecrementAsync(tenantId, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSkipped(logger, ex, tenantId.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quota counter unavailable; the monthly count is skipped for tenant {TenantId}")]
    private static partial void LogSkipped(ILogger logger, Exception exception, string tenantId);
}
