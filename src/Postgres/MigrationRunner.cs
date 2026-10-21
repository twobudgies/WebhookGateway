using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WebhookGateway.Postgres;

/// <summary>
/// Applies <c>db/migrations/NNNN_name.sql</c> in order, each in its own transaction, once. Forward-only: an applied
/// file that was edited afterwards is an error (checksum). An advisory lock keeps two runners from racing.
/// Run by the deploy pipeline (and <c>scripts/dev-up.sh</c> locally), never by a service at startup (docs/database.md).
/// </summary>
public sealed partial class MigrationRunner(NpgsqlDataSource dataSource, ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 7_301_945_001;

    /// <returns>The versions applied by this run.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(string directory, CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal).ToList();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await Execute(connection, "SELECT pg_advisory_lock(@k)", cancellationToken, ("k", AdvisoryLockKey)).ConfigureAwait(false);
        try
        {
            await Execute(connection, "CREATE SCHEMA IF NOT EXISTS webhook", cancellationToken).ConfigureAwait(false);
            await Execute(
                connection,
                "CREATE TABLE IF NOT EXISTS webhook.schema_migrations (version text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())",
                cancellationToken).ConfigureAwait(false);

            var applied = new Dictionary<string, string>();
            await using (var read = new NpgsqlCommand("SELECT version, checksum FROM webhook.schema_migrations", connection))
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    applied[reader.GetString(0)] = reader.GetString(1);
                }
            }

            var newlyApplied = new List<string>();
            foreach (var file in files)
            {
                var version = Path.GetFileNameWithoutExtension(file);
                var sql = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
                if (applied.TryGetValue(version, out var existing))
                {
                    if (existing != checksum)
                    {
                        LogChanged(logger, version);
                        throw new InvalidOperationException($"Migration {version} was applied before but its file has changed. Migrations are forward-only: add a new one.");
                    }

                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (var run = new NpgsqlCommand(sql, connection, transaction))
                {
                    await run.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var record = new NpgsqlCommand("INSERT INTO webhook.schema_migrations (version, checksum) VALUES (@v, @c)", connection, transaction))
                {
                    record.Parameters.AddWithValue("v", version);
                    record.Parameters.AddWithValue("c", checksum);
                    await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                LogApplied(logger, version);
                newlyApplied.Add(version);
            }

            LogDone(logger, newlyApplied.Count, files.Count);
            return newlyApplied;
        }
        finally
        {
            await Execute(connection, "SELECT pg_advisory_unlock(@k)", CancellationToken.None, ("k", AdvisoryLockKey)).ConfigureAwait(false);
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Version}")]
    private static partial void LogApplied(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations done: {Applied} applied, {Total} files")]
    private static partial void LogDone(ILogger logger, int applied, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration {Version} was applied before but its file changed")]
    private static partial void LogChanged(ILogger logger, string version);
}
