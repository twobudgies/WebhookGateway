namespace WebhookGateway.Ingest.Tenancy;

/// <summary>Resolves an API key hash to its tenant. Implementations cache; Ingest never hits a database per request.</summary>
public interface IApiKeyStore
{
    /// <returns>The tenant, or null when the key is unknown or revoked.</returns>
    ValueTask<TenantContext?> FindByKeyHashAsync(string keyHash, CancellationToken cancellationToken);
}
