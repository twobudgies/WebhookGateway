using System.Collections.Concurrent;
using System.Globalization;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Limits;

public sealed class InMemoryQuotaCounter : IQuotaCounter
{
    private readonly ConcurrentDictionary<string, long> _counts = new();

    public ValueTask<long> IncrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_counts.AddOrUpdate(Key(tenantId, now), 1, (_, v) => v + 1));

    public ValueTask DecrementAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _counts.AddOrUpdate(Key(tenantId, now), 0, (_, v) => Math.Max(0, v - 1));
        return ValueTask.CompletedTask;
    }

    private static string Key(TenantId tenantId, DateTimeOffset now) => $"{tenantId.Value}:{now.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture)}";
}
