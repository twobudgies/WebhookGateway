using WebhookGateway.Aws;
using WebhookGateway.FanOut;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Processing;
using WebhookGateway.FanOut.Routing;
using WebhookGateway.FanOut.Source;
using WebhookGateway.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddStructuredJsonConsole("FanOut");

// In-memory implementations until the AWS-backed ones are verified against emulators (roadmap P1-5b).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISubscriptionStore, InMemorySubscriptionStore>();
builder.Services.AddSingleton<IEventArchive, InMemoryEventArchive>();
builder.Services.AddSingleton<IDeliveryPublisher, InMemoryDeliveryPublisher>();
builder.Services.AddSingleton<IEventSource, InMemoryEventSource>();
builder.Services.AddSingleton<FanOutProcessor>();
builder.Services.AddSingleton<EventConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventConsumer>());

if (builder.Configuration.UseAws())
{
    builder.Services.AddAwsFanOut(builder.Configuration);
}

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup")
    .LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : "InMemory");
host.Run();
