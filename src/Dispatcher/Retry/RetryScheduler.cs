using Microsoft.Extensions.Options;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.Dispatcher.Retry;

/// <summary>
/// Moves deliveries that wait longer than the SQS delay limit from the due index back onto their tier queue
/// (docs/architecture.md section 6.1). Publish first, then flip the row to <c>RetryScheduled</c> (which removes it from
/// the index): a crash in between, or two instances racing, can only produce a duplicate message, which the
/// Dispatcher already tolerates (at-least-once, stable delivery ids).
/// </summary>
public sealed partial class RetryScheduler(
    IDueDeliveryIndex index,
    IDeliveryStateStore state,
    IRetryPublisher publisher,
    TimeProvider clock,
    IOptions<RetrySchedulerOptions> options,
    ILogger<RetryScheduler> logger) : BackgroundService
{
    private readonly RetrySchedulerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, _options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any single failure; due rows stay in the index and are picked up next poll.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPollFailed(logger, ex);
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>One poll. Exposed so tests can drive it without a hosted loop.</summary>
    /// <returns>The number of deliveries put back on a queue.</returns>
    public async ValueTask<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await index.GetDueAsync(now, _options.BatchSize, cancellationToken).ConfigureAwait(false);
        var moved = 0;
        foreach (var record in due)
        {
            try
            {
                await publisher.PublishAsync(record.Tier, NextMessage(record), TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                await state.PutAsync(record with { Status = Contracts.DeliveryStatus.RetryScheduled, UpdatedAt = now }, cancellationToken).ConfigureAwait(false);
                moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMoveFailed(logger, ex, record.TenantId.Value, record.DeliveryId.Value, record.Tier);
            }
        }

        if (due.Count > 0)
        {
            LogMoved(logger, moved, due.Count);
        }

        return moved;
    }

    private static DeliveryMessage NextMessage(DeliveryStateRecord record) =>
        new(
            DeliveryMessage.CurrentSchemaVersion,
            record.TenantId,
            record.EventId,
            record.DeliveryId,
            record.EndpointId,
            record.EventType,
            record.OccurredAt,
            record.Attempt + 1,
            record.CreatedAt,
            record.NextAttemptAt ?? record.UpdatedAt,
            record.Payload,
            record.PayloadRef);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry scheduler started (poll every {PollInterval})")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retry scheduler poll failed; will try again")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not move scheduled delivery {DeliveryId} of tenant {TenantId} to the {Tier} queue; it stays scheduled and is retried next poll")]
    private static partial void LogMoveFailed(ILogger logger, Exception exception, string tenantId, string deliveryId, Contracts.PlanTier tier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry scheduler moved {Moved} of {Due} due deliveries back to their queues")]
    private static partial void LogMoved(ILogger logger, int moved, int due);
}
