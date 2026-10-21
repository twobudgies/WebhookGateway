using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.FanOut.Archive;

namespace WebhookGateway.Postgres.Backend;

/// <summary>
/// Event archive in PostgreSQL (D53): one gzip JSON Lines batch per row, the same format as the S3 archive (D33). The pointer is
/// <c>pg://archive/&lt;key&gt;#&lt;line&gt;</c>. Rows expire after the retention period and are removed by the maintenance job.
/// </summary>
public sealed partial class PostgresEventArchive(
    NpgsqlDataSource db,
    TimeProvider clock,
    IOptions<BackendOptions> options,
    ILogger<PostgresEventArchive> logger) : IEventArchive
{
    public const string Scheme = "pg://archive/";
    private readonly string _workerId = Guid.NewGuid().ToString("N")[..12];
    private long _sequence;

    public async ValueTask<IReadOnlyDictionary<EventId, string>> WriteBatchAsync(IReadOnlyList<EventMessage> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        var now = clock.GetUtcNow();
        var key = $"{now:yyyy-MM-dd}/{now:HH}/{_workerId}-{Interlocked.Increment(ref _sequence)}";
        var refs = new Dictionary<EventId, string>(events.Count);
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            for (var line = 0; line < events.Count; line++)
            {
                await writer.WriteLineAsync(ContractJson.Serialize(events[line])).ConfigureAwait(false);
                refs[events[line].EventId] = $"{Scheme}{key}#{line}";
            }
        }

        await using var command = db.CreateCommand("INSERT INTO webhook.archive_batches (key, body, event_count, expires_at) VALUES (@key, @body, @count, @expires)");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("body", compressed.ToArray());
        command.Parameters.AddWithValue("count", events.Count);
        command.Parameters.AddWithValue("expires", now.Add(options.Value.ArchiveRetention));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(logger, events.Count, compressed.Length, key);
        return refs;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived {EventCount} events ({Bytes} bytes compressed) as batch {Key}")]
    private static partial void LogWritten(ILogger logger, int eventCount, long bytes, string key);
}

/// <summary>Inline payload when present, otherwise the event is read back from the archive batch named by <c>PayloadRef</c>.</summary>
public sealed partial class PostgresPayloadResolver(NpgsqlDataSource db, ILogger<PostgresPayloadResolver> logger) : IPayloadResolver
{
    public async ValueTask<JsonElement?> ResolveAsync(DeliveryMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload is not null)
        {
            return message.Payload;
        }

        var reference = message.PayloadRef;
        var hash = reference?.LastIndexOf('#') ?? -1;
        if (reference is null || !reference.StartsWith(PostgresEventArchive.Scheme, StringComparison.Ordinal) || hash < 0
            || !int.TryParse(reference[(hash + 1)..], out var line))
        {
            LogBadRef(logger, message.DeliveryId.Value);
            return null;
        }

        var key = reference[PostgresEventArchive.Scheme.Length..hash];
        await using var command = db.CreateCommand("SELECT body FROM webhook.archive_batches WHERE key = @key");
        command.Parameters.AddWithValue("key", key);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] body)
        {
            LogMissing(logger, message.DeliveryId.Value, key, line);
            return null;
        }

        using var stream = new MemoryStream(body);
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        for (var i = 0; await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } text; i++)
        {
            if (i == line)
            {
                var stored = ContractJson.Deserialize<EventMessage>(text);
                if (stored.EventId == message.EventId)
                {
                    return stored.Payload;
                }

                LogMismatch(logger, message.DeliveryId.Value, message.EventId.Value);
                return null;
            }
        }

        LogMissing(logger, message.DeliveryId.Value, key, line);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId} has an unreadable payload reference")]
    private static partial void LogBadRef(ILogger logger, string deliveryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId}: archive batch {Key} line {Line} not found")]
    private static partial void LogMissing(ILogger logger, string deliveryId, string key, int line);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId}: archive line does not belong to event {EventId}")]
    private static partial void LogMismatch(ILogger logger, string deliveryId, string eventId);
}
