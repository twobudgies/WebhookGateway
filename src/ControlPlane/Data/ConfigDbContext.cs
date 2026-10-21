using Microsoft.EntityFrameworkCore;

namespace WebhookGateway.ControlPlane.Data;

public sealed class PlanRow
{
    public string Code { get; set; } = string.Empty;

    public int MaxEndpoints { get; set; }
}

public sealed class TenantRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "active";

    public string PlanCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApiKeyRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string KeyHash { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class EndpointRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Status { get; set; } = "active";

    public byte[] SecretCiphertext { get; set; } = [];

    public string SecretKmsKeyId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SubscriptionRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EndpointId { get; set; }

    public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// Maps the existing <c>webhook</c> schema (docs/database.md); the schema itself comes from <c>db/migrations</c>, never
/// from EF (D43). Only the columns the ControlPlane touches are mapped.
/// </summary>
public sealed class ConfigDbContext(DbContextOptions<ConfigDbContext> options) : DbContext(options)
{
    public DbSet<PlanRow> Plans => Set<PlanRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<ApiKeyRow> ApiKeys => Set<ApiKeyRow>();

    public DbSet<EndpointRow> Endpoints => Set<EndpointRow>();

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("webhook");
        modelBuilder.Entity<PlanRow>(e =>
        {
            e.ToTable("plans");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.MaxEndpoints).HasColumnName("max_endpoints");
        });
        modelBuilder.Entity<TenantRow>(e =>
        {
            e.ToTable("tenants");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.PlanCode).HasColumnName("plan_code");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
        modelBuilder.Entity<ApiKeyRow>(e =>
        {
            e.ToTable("api_keys");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.KeyHash).HasColumnName("key_hash");
            e.Property(x => x.Prefix).HasColumnName("prefix");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        });
        modelBuilder.Entity<EndpointRow>(e =>
        {
            e.ToTable("endpoints");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.SecretCiphertext).HasColumnName("secret_ciphertext");
            e.Property(x => x.SecretKmsKeyId).HasColumnName("secret_kms_key_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
        modelBuilder.Entity<SubscriptionRow>(e =>
        {
            e.ToTable("subscriptions");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            e.Property(x => x.EventType).HasColumnName("event_type");
        });
    }
}
