using WebhookGateway.Aws;
using WebhookGateway.Core.Rollups;
using WebhookGateway.FanOut;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Processing;
using WebhookGateway.FanOut.Routing;
using WebhookGateway.FanOut.Source;
using WebhookGateway.Hosting;
using WebhookGateway.Postgres;
using WebhookGateway.Postgres.Backend;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddStructuredJsonConsole("FanOut");

// In-memory implementations until the AWS-backed ones are verified against emulators (roadmap P1-5b).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RollupAccumulator>();
builder.Services.AddSingleton<ISubscriptionStore, InMemorySubscriptionStore>();
builder.Services.AddSingleton<IEventArchive, InMemoryEventArchive>();
builder.Services.AddSingleton<IDeliveryPublisher, InMemoryDeliveryPublisher>();
builder.Services.AddSingleton<IEventSource, InMemoryEventSource>();
builder.Services.AddSingleton<FanOutProcessor>();
builder.Services.AddSingleton<EventConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventConsumer>());

if (builder.Configuration.UsePostgres())
{
    builder.Services.AddPostgresDataSource(builder.Configuration);
    builder.Services.AddSingleton<ISubscriptionStore, PostgresSubscriptionStore>();
}

if (builder.Configuration.UseAws())
{
    builder.Services.AddAwsFanOut(builder.Configuration);
    builder.Services.AddAwsRollups(builder.Configuration);
    builder.Services.Configure<RollupFlusherOptions>(builder.Configuration.GetSection(RollupFlusherOptions.SectionName));
    builder.Services.AddHostedService<RollupFlusher>();
}

if (builder.Configuration.UsePostgresBackend())
{
    builder.Services.AddPostgresFanOut(builder.Configuration);
    builder.Services.Configure<RollupFlusherOptions>(builder.Configuration.GetSection(RollupFlusherOptions.SectionName));
    builder.Services.AddHostedService<RollupFlusher>();
}

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup")
    .LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : builder.Configuration.UsePostgresBackend() ? "Postgres" : "InMemory");
host.Run();
