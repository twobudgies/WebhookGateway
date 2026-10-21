using Microsoft.EntityFrameworkCore;
using WebhookGateway.ApiDocs;
using WebhookGateway.Aws;
using WebhookGateway.ControlPlane.Api;
using WebhookGateway.ControlPlane.Data;
using WebhookGateway.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredJsonConsole("ControlPlane");

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

// The admin API needs the config database and a way to protect endpoint secrets; without them only /healthz runs.
var connectionString = builder.Configuration["Postgres:ConnectionString"];
var adminEnabled = !string.IsNullOrWhiteSpace(connectionString);
if (adminEnabled)
{
    builder.Services.AddDbContext<ConfigDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSecretProtection(builder.Configuration);
}

if (builder.Configuration.DocsEnabled())
{
    builder.Services.AddApiDocs(
        "WebhookGateway operator API",
        "Create customers (tenants), their API keys, endpoints and subscriptions. Click Authorize and paste your admin key (printed by the init container on first start), then Try it out. API keys and signing secrets are shown once, when created.",
        new ApiSecurity.HeaderKey("X-Admin-Key", "The operator admin key."));
}

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "ControlPlane" })).ExcludeFromDescription();
if (adminEnabled)
{
    AdminApi.Map(app);
}

if (builder.Configuration.DocsEnabled())
{
    app.UseApiDocs("WebhookGateway operator API");
}

app.Logger.LogServiceStarted(adminEnabled ? "Admin API on" : "Admin API off (no Postgres:ConnectionString)");
app.Run();
