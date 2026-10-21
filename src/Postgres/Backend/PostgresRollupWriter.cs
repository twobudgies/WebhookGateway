using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Core.Rollups;

namespace WebhookGateway.Postgres.Backend;

/// <summary>
/// Adds drained counters to <c>webhook.rollups</c> with <c>ON CONFLICT DO UPDATE col = col + excluded.col</c>, so many
/// workers can add to the same minute row. Same row layout as the DynamoDB writer (D41); rows that fail are returned.
/// </summary>
public sealed partial class PostgresRollupWriter(
    NpgsqlDataSource db,
    TimeProvider clock,
    IOptions<BackendOptions> options,
    ILogger<PostgresRollupWriter> logger) : IRollupWriter
{
    private static readonly string[] CounterNames =
    [
        "created", "attempts", "succeeded", "retried", "failed", "exhausted", "timeouts", "connection_ms", "rt_sum", "qw_sum",
        .. Enumerable.Range(0, 13).Select(i => $"rt{i}"),
        .. Enumerable.Range(0, 13).Select(i => $"qw{i}"),
    ];

    private static readonly string Sql = BuildSql();
    private readonly BackendOptions _options = options.Value;

    public async ValueTask<IReadOnlyDictionary<RollupKey, RollupRow>> WriteAsync(
        IReadOnlyDictionary<RollupKey, RollupRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var expires = clock.GetUtcNow().Add(_options.RollupMinuteRetention);
        var failed = new Dictionary<RollupKey, RollupRow>();
        Exception? lastError = null;
        foreach (var (key, row) in rows)
        {
            try
            {
                await using var command = db.CreateCommand(Sql);
                command.Parameters.AddWithValue("pk", $"{key.TenantId.Value}#{key.EndpointKey}");
                command.Parameters.AddWithValue("sk", $"1m#{key.Minute.UtcDateTime:yyyyMMddHHmm}");
                command.Parameters.AddWithValue("expires", expires);
                var values = Values(row);
                for (var i = 0; i < CounterNames.Length; i++)
                {
                    command.Parameters.AddWithValue($"v{i}", values[i]);
                }

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed[key] = row;
                lastError = ex;
            }
        }

        if (lastError is not null)
        {
            LogFlushFailed(logger, lastError, failed.Count, rows.Count);
        }
        else
        {
            LogFlushed(logger, rows.Count);
        }

        return failed;
    }

    private static long[] Values(RollupRow row)
    {
        var values = new List<long>
        {
            row.Created, row.Attempts, row.Succeeded, row.Retried, row.Failed, row.Exhausted, row.Timeouts,
            row.ConnectionMs, row.ResponseTime.SumMs, row.QueueWait.SumMs,
        };
        values.AddRange(row.ResponseTime.Counts);
        values.AddRange(row.QueueWait.Counts);
        return [.. values];
    }

    private static string BuildSql()
    {
        var columns = string.Join(", ", CounterNames);
        var parameters = string.Join(", ", CounterNames.Select((_, i) => $"@v{i}"));
        var updates = string.Join(", ", CounterNames.Select(c => $"{c} = webhook.rollups.{c} + EXCLUDED.{c}"));
        return new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"INSERT INTO webhook.rollups (pk, sk, expires_at, {columns}) VALUES (@pk, @sk, @expires, {parameters}) ")
            .Append(CultureInfo.InvariantCulture, $"ON CONFLICT (pk, sk) DO UPDATE SET {updates}, expires_at = EXCLUDED.expires_at")
            .ToString();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rollups flushed: {RowCount} rows")]
    private static partial void LogFlushed(ILogger logger, int rowCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollup flush failed for {Failed} of {Total} rows; they are kept for the next flush")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception, int failed, int total);
}
