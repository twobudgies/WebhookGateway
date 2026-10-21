using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace WebhookGateway.Postgres.Backend;

/// <summary>A message leased from a queue. <see cref="Receipt"/> proves the lease when deleting it.</summary>
public sealed record QueueMessage(long Id, string Receipt, string Body, int ReceiveCount);

/// <summary>
/// A durable queue on one PostgreSQL table with the semantics the services rely on from SQS (D53): messages can be delayed,
/// a received message is invisible for the visibility timeout and reappears if it is not deleted, and after
/// <see cref="MaxReceives"/> receives it moves to the dead-letter queue <c>&lt;queue&gt;-dlq</c>. Receiving uses
/// <c>FOR UPDATE SKIP LOCKED</c>, so many workers can read the same queue without blocking each other.
/// </summary>
public sealed partial class PostgresMessageStore(NpgsqlDataSource db, ILogger<PostgresMessageStore> logger)
{
    public const int MaxReceives = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async ValueTask SendAsync(string queue, IReadOnlyList<(string Body, TimeSpan Delay)> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        await using var command = db.CreateCommand(
            "INSERT INTO webhook.queue_messages (queue, body, visible_at) SELECT @queue, b, now() + make_interval(secs => d) FROM unnest(@bodies, @delays) AS t(b, d)");
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.Add(new NpgsqlParameter("bodies", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = messages.Select(m => m.Body).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("delays", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = messages.Select(m => m.Delay.TotalSeconds).ToArray() });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits up to <paramref name="wait"/> for messages (long poll) and leases up to <paramref name="maxCount"/> of them.</summary>
    public async ValueTask<IReadOnlyList<QueueMessage>> ReceiveAsync(
        string queue,
        int maxCount,
        TimeSpan visibilityTimeout,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + wait;
        while (true)
        {
            await DeadLetterExhaustedAsync(queue, cancellationToken).ConfigureAwait(false);
            var messages = await LeaseAsync(queue, maxCount, visibilityTimeout, cancellationToken).ConfigureAwait(false);
            if (messages.Count > 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return messages;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DeleteAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var command = db.CreateCommand("DELETE FROM webhook.queue_messages WHERE id = @id AND receipt = @receipt");
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("receipt", Guid.Parse(message.Receipt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<QueueMessage>> LeaseAsync(string queue, int maxCount, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(
            """
            UPDATE webhook.queue_messages m
            SET visible_at = now() + make_interval(secs => @visibility), receive_count = m.receive_count + 1, receipt = gen_random_uuid()
            WHERE m.id IN (
                SELECT id FROM webhook.queue_messages
                WHERE queue = @queue AND visible_at <= now() AND receive_count < @max
                ORDER BY id LIMIT @limit FOR UPDATE SKIP LOCKED)
            RETURNING m.id, m.receipt, m.body, m.receive_count
            """);
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.AddWithValue("visibility", visibilityTimeout.TotalSeconds);
        command.Parameters.AddWithValue("max", MaxReceives);
        command.Parameters.AddWithValue("limit", maxCount);
        var result = new List<QueueMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new QueueMessage(reader.GetInt64(0), reader.GetGuid(1).ToString(), reader.GetString(2), reader.GetInt32(3)));
        }

        return result;
    }

    private async ValueTask DeadLetterExhaustedAsync(string queue, CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(
            "UPDATE webhook.queue_messages SET queue = queue || '-dlq', receive_count = 0, visible_at = now() WHERE queue = @queue AND visible_at <= now() AND receive_count >= @max");
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.AddWithValue("max", MaxReceives);
        var moved = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (moved > 0)
        {
            LogDeadLettered(logger, moved, queue);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} messages from queue {Queue} failed 5 times and moved to the dead-letter queue")]
    private static partial void LogDeadLettered(ILogger logger, int count, string queue);
}
