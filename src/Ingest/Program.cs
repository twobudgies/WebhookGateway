using WebhookGateway.Hosting;
using WebhookGateway.Ingest.Api;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Publishing;
using WebhookGateway.Ingest.Tenancy;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredJsonConsole("Ingest");

builder.Services.Configure<ConfigApiKeyOptions>(builder.Configuration.GetSection(ConfigApiKeyOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApiKeyStore, ConfigApiKeyStore>();
builder.Services.AddSingleton<IRateLimiter, LocalRateLimiter>();
builder.Services.AddSingleton<InMemoryEventPublisher>();
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>());

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "Ingest" }));
EventsEndpoint.Map(app);

app.Run();
