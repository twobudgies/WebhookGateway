using Microsoft.Extensions.Options;
using WebhookGateway.Aws;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;
using WebhookGateway.Dispatcher.Source;
using WebhookGateway.Hosting;
using WebhookGateway.Postgres;
using WebhookGateway.Postgres.Backend;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddStructuredJsonConsole("Dispatcher");

builder.Services.Configure<SenderOptions>(builder.Configuration.GetSection(SenderOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RollupAccumulator>();
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
if (builder.Configuration.UsePostgres())
{
    builder.Services.AddPostgresDataSource(builder.Configuration);
    builder.Services.AddSecretProtection(builder.Configuration);
    builder.Services.AddSingleton<IEndpointStore, PostgresEndpointStore>();
}

// The consumer needs the queues, the archive and delivery_state, so it only runs with Backend:Mode=Aws
// (Moto locally, see dev/README.md). The retry scheduler and the rollup flusher run next to it.
if (builder.Configuration.UseAws() || builder.Configuration.UsePostgresBackend())
{
    builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));
    builder.Services.Configure<DeliveryConsumerOptions>(builder.Configuration.GetSection(DeliveryConsumerOptions.SectionName));
    if (builder.Configuration.UseAws())
    {
        builder.Services.AddAwsDispatcher(builder.Configuration);
        builder.Services.AddAwsRollups(builder.Configuration);
    }
    else
    {
        builder.Services.AddPostgresDispatcher(builder.Configuration);
    }

    builder.Services.Configure<RollupFlusherOptions>(builder.Configuration.GetSection(RollupFlusherOptions.SectionName));
    builder.Services.AddHostedService<RollupFlusher>();
    builder.Services.AddSingleton<DeliveryProcessor>();
    builder.Services.AddSingleton<IDeliveryProcessor>(sp => sp.GetRequiredService<DeliveryProcessor>());
    builder.Services.Configure<RetrySchedulerOptions>(builder.Configuration.GetSection(RetrySchedulerOptions.SectionName));
    builder.Services.AddHostedService<DeliveryConsumer>();
    builder.Services.AddHostedService<RetryScheduler>();
}

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup")
    .LogServiceStarted(builder.Configuration.UseAws() ? "Aws" : builder.Configuration.UsePostgresBackend() ? "Postgres" : "InMemory (no queue consumer)");
host.Run();
