using System.Globalization;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.Aws.DynamoDb;

/// <summary>
/// <c>delivery_state</c> (docs/data-plane.md section 4): only non-success deliveries. Key <c>tenantId#deliveryId</c>;
/// gsi1 lists a tenant's deliveries by status, gsi2 by endpoint, gsi3 is the scheduler's due index (only rows in
/// status <see cref="DeliveryStatus.Scheduled"/> carry it).
/// </summary>
public sealed partial class DynamoDbDeliveryStateStore(
    IAmazonDynamoDB dynamo,
    TimeProvider clock,
    IOptions<DynamoDbOptions> options,
    ILogger<DynamoDbDeliveryStateStore> logger) : IDeliveryStateStore
{
    private readonly DynamoDbOptions _options = options.Value;

    public async ValueTask<DeliveryStateRecord?> GetAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        var response = await dynamo.GetItemAsync(
            new GetItemRequest
            {
                TableName = _options.DeliveryStateTable,
                Key = Key(tenantId, deliveryId),
                ConsistentRead = true,
            },
            cancellationToken).ConfigureAwait(false);
        return response.IsItemSet ? FromItem(response.Item) : null;
    }

    public async ValueTask PutAsync(DeliveryStateRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S(PartitionKey(record.TenantId, record.DeliveryId)),
            ["tenantId"] = S(record.TenantId.Value),
            ["deliveryId"] = S(record.DeliveryId.Value),
            ["endpointId"] = S(record.EndpointId.Value),
            ["eventId"] = S(record.EventId.Value),
            ["eventType"] = S(record.EventType),
            ["occurredAt"] = S(Iso(record.OccurredAt)),
            ["status"] = S(record.Status.ToString()),
            ["attempt"] = N(record.Attempt),
            ["createdAt"] = S(Iso(record.CreatedAt)),
            ["updatedAt"] = S(Iso(record.UpdatedAt)),
            ["gsi1pk"] = S($"{record.TenantId.Value}#{record.Status}"),
            ["gsi1sk"] = S(Iso(record.UpdatedAt)),
            ["gsi2pk"] = S($"{record.TenantId.Value}#{record.EndpointId.Value}"),
            ["gsi2sk"] = S(Iso(record.UpdatedAt)),
            ["ttl"] = N(clock.GetUtcNow().Add(_options.DeliveryStateRetention).ToUnixTimeSeconds()),
        };
        if (record.NextAttemptAt is { } next)
        {
            item["nextAttemptAt"] = S(Iso(next));
            if (record.Status == DeliveryStatus.Scheduled)
            {
                item["gsi3pk"] = S($"due#{Bucket(record.DeliveryId)}");
                item["gsi3sk"] = S(Iso(next));
            }
        }

        if (record.LastStatusCode is { } code)
        {
            item["lastStatusCode"] = N(code);
        }

        if (record.LastError is not null)
        {
            item["lastError"] = S(record.LastError);
        }

        if (record.Payload is { } payload)
        {
            item["payload"] = S(payload.GetRawText());
        }

        if (record.PayloadRef is not null)
        {
            item["payloadRef"] = S(record.PayloadRef);
        }

        await dynamo.PutItemAsync(new PutItemRequest { TableName = _options.DeliveryStateTable, Item = item }, cancellationToken).ConfigureAwait(false);
        LogStored(logger, record.TenantId.Value, record.DeliveryId.Value, record.Status, record.Attempt);
    }

    public async ValueTask DeleteAsync(TenantId tenantId, DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        await dynamo.DeleteItemAsync(
            new DeleteItemRequest { TableName = _options.DeliveryStateTable, Key = Key(tenantId, deliveryId) },
            cancellationToken).ConfigureAwait(false);
        LogDeleted(logger, tenantId.Value, deliveryId.Value);
    }

    private static Dictionary<string, AttributeValue> Key(TenantId tenantId, DeliveryId deliveryId) =>
        new() { ["pk"] = S(PartitionKey(tenantId, deliveryId)) };

    private static string PartitionKey(TenantId tenantId, DeliveryId deliveryId) => $"{tenantId.Value}#{deliveryId.Value}";

    /// <summary>Stable across processes (unlike <c>string.GetHashCode</c>): FNV-1a over the delivery id.</summary>
    private int Bucket(DeliveryId deliveryId)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(deliveryId.Value))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (int)(hash % (uint)_options.DueBuckets);
    }

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static AttributeValue S(string value) => new() { S = value };

    private static AttributeValue N(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static DeliveryStateRecord FromItem(Dictionary<string, AttributeValue> item) =>
        new(
            TenantId.Parse(item["tenantId"].S),
            DeliveryId.Parse(item["deliveryId"].S),
            EndpointId.Parse(item["endpointId"].S),
            EventId.Parse(item["eventId"].S),
            item["eventType"].S,
            ParseIso(item["occurredAt"].S),
            Enum.Parse<DeliveryStatus>(item["status"].S),
            int.Parse(item["attempt"].N, CultureInfo.InvariantCulture),
            item.TryGetValue("nextAttemptAt", out var next) ? ParseIso(next.S) : null,
            item.TryGetValue("lastStatusCode", out var code) ? int.Parse(code.N, CultureInfo.InvariantCulture) : null,
            item.TryGetValue("lastError", out var error) ? error.S : null,
            item.TryGetValue("payload", out var payload) ? JsonDocument.Parse(payload.S).RootElement.Clone() : null,
            item.TryGetValue("payloadRef", out var payloadRef) ? payloadRef.S : null,
            ParseIso(item["createdAt"].S),
            ParseIso(item["updatedAt"].S));

    [LoggerMessage(Level = LogLevel.Debug, Message = "delivery_state stored {DeliveryId} of tenant {TenantId}: {Status}, attempt {Attempt}")]
    private static partial void LogStored(ILogger logger, string tenantId, string deliveryId, DeliveryStatus status, int attempt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "delivery_state deleted {DeliveryId} of tenant {TenantId}")]
    private static partial void LogDeleted(ILogger logger, string tenantId, string deliveryId);
}
