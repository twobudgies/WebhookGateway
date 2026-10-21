using WebhookGateway.Aws;
using WebhookGateway.Hosting;
using WebhookGateway.Ingest.Api;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Publishing;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Postgres;
using WebhookGateway.Valkey;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredJsonConsole("Ingest");

builder.Services.Configure<ConfigApiKeyOptions>(builder.Configuration.GetSection(ConfigApiKeyOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApiKeyStore, ConfigApiKeyStore>();
builder.Services.Configure<QuotaOptions>(builder.Configuration.GetSection(QuotaOptions.SectionName));
builder.Services.AddSingleton<LocalRateLimiter>();
builder.Services.AddSingleton<IRateLimiter>(sp => sp.GetRequiredService<LocalRateLimiter>());
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<IQuotaCounter, InMemoryQuotaCounter>();
builder.Services.AddSingleton<InMemoryEventPublisher>();
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>());

if (builder.Configuration.UseAws())
{
    builder.Services.AddAwsIngest(builder.Configuration);
}

// With Postgres:ConnectionString set, API keys and plan limits come from the config database (cached 30 s).
if (builder.Configuration.UsePostgres())
{
    builder.Services.AddPostgresDataSource(builder.Configuration);
    builder.Services.AddSingleton<IApiKeyStore, PostgresApiKeyStore>();
}

// With Valkey:ConnectionString set, limits are shared across Ingest tasks; each falls back to local behavior if Valkey is down.
if (builder.Configuration.UseValkey())
{
    builder.Services.AddValkeyConnection(builder.Configuration);
    builder.Services.AddSingleton<IRateLimiter>(sp => new ResilientRateLimiter(
        sp.GetRequiredService<ValkeyRateLimiter>(),
        sp.GetRequiredService<LocalRateLimiter>(),
        sp.GetRequiredService<ILogger<ResilientRateLimiter>>()));
    builder.Services.AddSingleton<IIdempotencyStore>(sp => new ResilientIdempotencyStore(
        sp.GetRequiredService<ValkeyIdempotencyStore>(),
        sp.GetRequiredService<ILogger<ResilientIdempotencyStore>>()));
    builder.Services.AddSingleton<IQuotaCounter>(sp => new ResilientQuotaCounter(
        sp.GetRequiredService<ValkeyQuotaCounter>(),
        sp.GetRequiredService<ILogger<ResilientQuotaCounter>>()));
}

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "Ingest" }));
EventsEndpoint.Map(app);

app.Logger.LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : "InMemory");
app.Run();
