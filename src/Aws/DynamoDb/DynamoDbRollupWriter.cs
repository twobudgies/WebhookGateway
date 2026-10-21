using System.Globalization;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookGateway.Core.Rollups;

namespace WebhookGateway.Aws.DynamoDb;

/// <summary>
/// Writes drained rollups to the <c>rollups</c> table with atomic ADD (docs/data-plane.md section 4), so many workers
/// can add to the same minute row. Key <c>tenantId#endpointKey</c> / <c>1m#yyyyMMddHHmm</c>. A failure throws; the
/// caller decides whether to merge the rows back and retry.
/// </summary>
public sealed partial class DynamoDbRollupWriter(
    IAmazonDynamoDB dynamo,
    TimeProvider clock,
    IOptions<DynamoDbOptions> options,
    ILogger<DynamoDbRollupWriter> logger)
{
    private readonly DynamoDbOptions _options = options.Value;

    public async ValueTask WriteAsync(IReadOnlyDictionary<RollupKey, RollupRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var ttl = clock.GetUtcNow().Add(_options.RollupMinuteRetention).ToUnixTimeSeconds();
        var written = 0;
        try
        {
            foreach (var (key, row) in rows)
            {
                await dynamo.UpdateItemAsync(BuildUpdate(key, row, ttl), cancellationToken).ConfigureAwait(false);
                written++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFlushFailed(logger, ex, written, rows.Count);
            throw;
        }

        LogFlushed(logger, written);
    }

    private UpdateItemRequest BuildUpdate(RollupKey key, RollupRow row, long ttl)
    {
        var adds = new List<(string Name, long Value)>
        {
            ("created", row.Created),
            ("attempts", row.Attempts),
            ("succeeded", row.Succeeded),
            ("retried", row.Retried),
            ("failed", row.Failed),
            ("exhausted", row.Exhausted),
            ("timeouts", row.Timeouts),
            ("connectionMs", row.ConnectionMs),
            ("rtSum", row.ResponseTime.SumMs),
            ("qwSum", row.QueueWait.SumMs),
        };
        for (var i = 0; i < row.ResponseTime.Counts.Count; i++)
        {
            adds.Add(($"rt{i}", row.ResponseTime.Counts[i]));
            adds.Add(($"qw{i}", row.QueueWait.Counts[i]));
        }

        var names = new Dictionary<string, string>();
        var values = new Dictionary<string, AttributeValue>();
        var expression = new StringBuilder("ADD ");
        for (var i = 0; i < adds.Count; i++)
        {
            names[$"#a{i}"] = adds[i].Name;
            values[$":v{i}"] = new AttributeValue { N = adds[i].Value.ToString(CultureInfo.InvariantCulture) };
            expression.Append(i == 0 ? string.Empty : ", ").Append($"#a{i} :v{i}");
        }

        names["#ttl"] = "ttl";
        values[":ttl"] = new AttributeValue { N = ttl.ToString(CultureInfo.InvariantCulture) };
        expression.Append(" SET #ttl = :ttl");

        return new UpdateItemRequest
        {
            TableName = _options.RollupsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new() { S = $"{key.TenantId.Value}#{key.EndpointKey}" },
                ["sk"] = new() { S = $"1m#{key.Minute.UtcDateTime:yyyyMMddHHmm}" },
            },
            UpdateExpression = expression.ToString(),
            ExpressionAttributeNames = names,
            ExpressionAttributeValues = values,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rollups flushed: {RowCount} rows")]
    private static partial void LogFlushed(ILogger logger, int rowCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollup flush failed after {Written} of {Total} rows; counters of unwritten rows must be re-added by the caller")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception, int written, int total);
}
