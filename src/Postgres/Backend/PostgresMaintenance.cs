using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WebhookGateway.Postgres.Backend;

/// <summary>
/// Deletes expired rows (delivery state, archive batches, rollup minutes, idempotency keys) and old dead-lettered messages once an
/// hour. Safe to run in several instances: every statement is an idempotent delete.
/// </summary>
public sealed partial class PostgresMaintenance(NpgsqlDataSource db, ILogger<PostgresMaintenance> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One cleanup pass. Exposed so tests can run it.</summary>
    public async ValueTask<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var (table, column) in new[]
        {
            ("delivery_state", "expires_at"),
            ("archive_batches", "expires_at"),
            ("rollups", "expires_at"),
            ("idempotency_keys", "expires_at"),
        })
        {
            await using var command = db.CreateCommand($"DELETE FROM webhook.{table} WHERE {column} <= now()");
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
            {
                LogDeleted(logger, deleted, table);
            }

            total += deleted;
        }

        await using var dlq = db.CreateCommand("DELETE FROM webhook.queue_messages WHERE queue LIKE '%-dlq' AND enqueued_at <= now() - interval '14 days'");
        total += await dlq.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return total;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance removed {Count} expired rows from {Table}")]
    private static partial void LogDeleted(ILogger logger, int count, string table);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance pass failed; will try again in an hour")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
