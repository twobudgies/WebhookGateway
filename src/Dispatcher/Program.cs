using Microsoft.Extensions.Options;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;
using WebhookGateway.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddStructuredJsonConsole("Dispatcher");

builder.Services.Configure<SenderOptions>(builder.Configuration.GetSection(SenderOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(RetryPolicy.CreateDefault());
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<SenderOptions>>().Value;
    var handler = SafeHttpHandler.Create(options.ConnectTimeout, options.MaxConnectionsPerServer);
    return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
});
builder.Services.AddSingleton<WebhookSender>();

// Queue consumption, delivery state and rollups are wired in later roadmap items (P1-6b, P1-7, P1-10).
var host = builder.Build();
host.Run();
