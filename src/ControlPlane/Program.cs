using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "ControlPlane" }));
if (adminEnabled)
{
    AdminApi.Map(app);
}

app.Logger.LogServiceStarted(adminEnabled ? "Admin API on" : "Admin API off (no Postgres:ConnectionString)");
app.Run();
