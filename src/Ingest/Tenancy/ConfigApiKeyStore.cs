using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;

namespace WebhookGateway.Ingest.Tenancy;

public sealed class ConfigApiKeyOptions
{
    public const string SectionName = "Ingest:ApiKeys";

    public List<ConfigApiKeyEntry> Keys { get; init; } = [];
}

public sealed class ConfigApiKeyEntry
{
    public string KeyHash { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public PlanTier Plan { get; init; } = PlanTier.Basic;

    public TenantStatus Status { get; init; } = TenantStatus.Active;
}

/// <summary>
/// Static key list from configuration. For local development and tests only; the production store reads the
/// config database (roadmap P2-1) through the same interface.
/// </summary>
public sealed class ConfigApiKeyStore : IApiKeyStore
{
    private readonly Dictionary<string, TenantContext> _byHash;

    public ConfigApiKeyStore(IOptions<ConfigApiKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _byHash = options.Value.Keys.ToDictionary(
            k => k.KeyHash,
            k => new TenantContext(TenantId.Parse(k.TenantId), k.Plan, k.Status, PlanLimits.For(k.Plan)),
            StringComparer.Ordinal);
    }

    public ValueTask<TenantContext?> FindByKeyHashAsync(string keyHash, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_byHash.GetValueOrDefault(keyHash));
}
