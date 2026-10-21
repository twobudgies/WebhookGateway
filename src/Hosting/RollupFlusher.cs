using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookGateway.Core.Rollups;

namespace WebhookGateway.Hosting;

public sealed class RollupFlusherOptions
{
    public const string SectionName = "Rollups";

    /// <summary>How often the in-memory counters are written (docs/data-plane.md section 4: every 10 seconds).</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Drains the in-memory counters on an interval and hands them to the <see cref="IRollupWriter"/>. Rows that could not
/// be written are put back and go out with the next flush, so a short outage loses nothing; one last flush runs on
/// shutdown. Write volume follows the number of active endpoints, not the event rate.
/// </summary>
public sealed partial class RollupFlusher(
    RollupAccumulator accumulator,
    IRollupWriter writer,
    IOptions<RollupFlusherOptions> options,
    ILogger<RollupFlusher> logger) : BackgroundService
{
    private readonly RollupFlusherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, _options.FlushInterval);
        using var timer = new PeriodicTimer(_options.FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: fall through to the last flush.
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>One flush. Exposed so tests can drive it without a timer.</summary>
    /// <returns>The number of rows that could not be written and were kept for the next flush.</returns>
    public async ValueTask<int> FlushAsync(CancellationToken cancellationToken)
    {
        var rows = accumulator.Drain();
        if (rows.Count == 0)
        {
            return 0;
        }

        try
        {
            var failed = await writer.WriteAsync(rows, cancellationToken).ConfigureAwait(false);
            if (failed.Count > 0)
            {
                accumulator.Restore(failed);
            }

            return failed.Count;
        }
#pragma warning disable CA1031 // Whatever went wrong, the drained counters must not be lost.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            accumulator.Restore(rows);
            LogFlushError(logger, ex, rows.Count);
            return rows.Count;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rollup flusher started (every {FlushInterval})")]
    private static partial void LogStarted(ILogger logger, TimeSpan flushInterval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollup flush failed for {RowCount} rows; kept for the next flush")]
    private static partial void LogFlushError(ILogger logger, Exception exception, int rowCount);
}
