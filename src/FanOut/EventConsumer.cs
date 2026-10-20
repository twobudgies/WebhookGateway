using WebhookGateway.FanOut.Processing;
using WebhookGateway.FanOut.Source;

namespace WebhookGateway.FanOut;

/// <summary>
/// Receive, process, acknowledge. An event is acknowledged only after its deliveries are published, so a failure
/// leaves it on the queue for redelivery (at-least-once; delivery ids are deterministic, so duplicates are harmless).
/// </summary>
public sealed partial class EventConsumer(IEventSource source, FanOutProcessor processor, ILogger<EventConsumer> logger)
    : BackgroundService
{
    public const int BatchSize = 10;

    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any single failure; the batch stays unacknowledged and is retried.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogBatchFailed(logger, ex);
                await Task.Delay(FailureBackoff, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Handles one receive cycle. Exposed so tests can drive it without a hosted loop.</summary>
    public async ValueTask<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var received = await source.ReceiveAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        if (received.Count == 0)
        {
            return 0;
        }

        var deliveries = await processor.ProcessAsync([.. received.Select(r => r.Message)], cancellationToken).ConfigureAwait(false);
        await source.AcknowledgeAsync(received, cancellationToken).ConfigureAwait(false);
        return deliveries;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fan-out consumer started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fan-out batch failed; it stays on the queue and will be redelivered")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}
