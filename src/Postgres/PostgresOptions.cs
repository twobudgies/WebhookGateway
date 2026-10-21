namespace WebhookGateway.Postgres;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>Npgsql connection string. Empty means the config database is not used (in-memory stores).</summary>
    public string? ConnectionString { get; init; }

    /// <summary>How long a loaded configuration is served from memory (docs/database.md: 30 s).</summary>
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Unknown keys and tenants are remembered briefly so bad credentials cannot hammer the database.</summary>
    public TimeSpan NegativeCacheTtl { get; init; } = TimeSpan.FromSeconds(5);
}
