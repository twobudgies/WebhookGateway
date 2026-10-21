using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.Postgres;

/// <summary>
/// What the Dispatcher needs to call an endpoint: URL and signing secret (decrypted, then kept in memory for the cache
/// TTL so KMS is called at most once per endpoint per TTL). Disabled or deleted endpoints resolve to null.
/// </summary>
public sealed partial class PostgresEndpointStore : IEndpointStore
{
    private readonly NpgsqlDataSource _db;
    private readonly ISecretProtector _protector;
    private readonly ILogger<PostgresEndpointStore> _logger;
    private readonly TtlCache<(TenantId, EndpointId), EndpointTarget> _cache;

    public PostgresEndpointStore(
        NpgsqlDataSource db,
        ISecretProtector protector,
        TimeProvider clock,
        IOptions<PostgresOptions> options,
        ILogger<PostgresEndpointStore> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
        _cache = new TtlCache<(TenantId, EndpointId), EndpointTarget>(clock, options.Value.CacheTtl, options.Value.NegativeCacheTtl);
    }

    public ValueTask<EndpointTarget?> GetAsync(TenantId tenantId, EndpointId endpointId, CancellationToken cancellationToken) =>
        _cache.GetOrLoadAsync((tenantId, endpointId), LoadAsync, cancellationToken);

    private async ValueTask<EndpointTarget?> LoadAsync((TenantId Tenant, EndpointId Endpoint) key, CancellationToken cancellationToken)
    {
        await using var command = _db.CreateCommand(
            "SELECT url, secret_ciphertext, secret_kms_key_id FROM webhook.endpoints WHERE id = @id AND tenant_id = @tenant AND status = 'active'");
        command.Parameters.AddWithValue("id", ConfigIds.Uuid(key.Endpoint));
        command.Parameters.AddWithValue("tenant", ConfigIds.Uuid(key.Tenant));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var url = reader.GetString(0);
        var secret = new ProtectedSecret((byte[])reader[1], reader.GetString(2));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            LogBadUrl(_logger, key.Endpoint.Value);
            return null;
        }

        var plaintext = await _protector.UnprotectAsync(secret, cancellationToken).ConfigureAwait(false);
        return new EndpointTarget(key.Endpoint, uri, [SigningSecret.Parse(plaintext)]);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Endpoint {EndpointId} has a stored URL that is not valid; treated as unavailable")]
    private static partial void LogBadUrl(ILogger logger, string endpointId);
}
