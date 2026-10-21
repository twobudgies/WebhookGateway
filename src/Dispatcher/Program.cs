using Microsoft.Extensions.Options;
using WebhookGateway.Aws;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;
using WebhookGateway.Dispatcher.Source;
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
builder.Services.AddSingleton<IWebhookSender>(sp => sp.GetRequiredService<WebhookSender>());

// Endpoint configuration comes from the config database once the ControlPlane exists (P2-1); until then the store is empty.
builder.Services.AddSingleton<IEndpointStore, InMemoryEndpointStore>();

// The consumer needs the queues, the archive and delivery_state, so it only runs with Backend:Mode=Aws
// (Moto locally, see dev/README.md). Rollup flushing and the retry scheduler are later roadmap items (P1-10, P1-7).
if (builder.Configuration.UseAws())
{
    builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));
    builder.Services.Configure<DeliveryConsumerOptions>(builder.Configuration.GetSection(DeliveryConsumerOptions.SectionName));
    builder.Services.AddAwsDispatcher(builder.Configuration);
    builder.Services.AddSingleton<DeliveryProcessor>();
    builder.Services.AddSingleton<IDeliveryProcessor>(sp => sp.GetRequiredService<DeliveryProcessor>());
    builder.Services.AddHostedService<DeliveryConsumer>();
}

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup")
    .LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : "InMemory (no queue consumer)");
host.Run();
