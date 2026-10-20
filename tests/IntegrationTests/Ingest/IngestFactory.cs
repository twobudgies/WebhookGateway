using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Ingest;
using WebhookGateway.Ingest.Publishing;
using WebhookGateway.Ingest.Tenancy;

namespace WebhookGateway.IntegrationTests.Ingest;

/// <summary>Ingest in memory with three tenants: active Basic, suspended, and one unknown key.</summary>
public sealed class IngestFactory : WebApplicationFactory<IngestApiMarker>
{
    public const string ActiveKey = "wgk_test_active";
    public const string SuspendedKey = "wgk_test_suspended";

    public TenantId ActiveTenant { get; } = TenantId.New();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

    public InMemoryEventPublisher Published => Services.GetRequiredService<InMemoryEventPublisher>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingest:ApiKeys:Keys:0:KeyHash"] = ApiKeyHasher.Hash(ActiveKey),
            ["Ingest:ApiKeys:Keys:0:TenantId"] = ActiveTenant.Value,
            ["Ingest:ApiKeys:Keys:0:Plan"] = nameof(PlanTier.Basic),
            ["Ingest:ApiKeys:Keys:1:KeyHash"] = ApiKeyHasher.Hash(SuspendedKey),
            ["Ingest:ApiKeys:Keys:1:TenantId"] = TenantId.New().Value,
            ["Ingest:ApiKeys:Keys:1:Plan"] = nameof(PlanTier.Basic),
            ["Ingest:ApiKeys:Keys:1:Status"] = nameof(TenantStatus.Suspended),
        }));
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
    }
}
