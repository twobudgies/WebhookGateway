using WebhookGateway.Hosting;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredJsonConsole("ControlPlane");
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "ControlPlane" }));

app.Run();
