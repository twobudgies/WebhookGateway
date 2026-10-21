using WebhookGateway.ApiDocs;
using WebhookGateway.Aws;
using WebhookGateway.Hosting;
using WebhookGateway.Ingest.Api;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Publishing;
using WebhookGateway.Ingest.Tenancy;
using WebhookGateway.Postgres;
using WebhookGateway.Postgres.Backend;
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

// Backend:Mode=Postgres: events go to a PostgreSQL queue, idempotency keys and the monthly count live in PostgreSQL (D53).
if (builder.Configuration.UsePostgresBackend())
{
    builder.Services.AddPostgresIngest(builder.Configuration);
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

if (builder.Configuration.DocsEnabled())
{
    builder.Services.AddApiDocs(
        "WebhookGateway events API",
        "Send events here; WebhookGateway delivers them to your customers' endpoints. Authenticate with a customer API key (wgk_live_...), created with the operator API. Click Authorize, paste the key, then Try it out.",
        new ApiSecurity.Bearer("A customer API key created with the operator API, sent as 'Authorization: Bearer wgk_live_...'."));
}

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "Ingest" })).ExcludeFromDescription();
EventsEndpoint.Map(app);
if (builder.Configuration.DocsEnabled())
{
    app.UseApiDocs("WebhookGateway events API");
}

app.Logger.LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : builder.Configuration.UsePostgresBackend() ? "Postgres" : "InMemory");
app.Run();
