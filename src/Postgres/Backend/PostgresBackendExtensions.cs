using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Source;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Source;
using WebhookGateway.Ingest.Limits;
using WebhookGateway.Ingest.Publishing;

namespace WebhookGateway.Postgres.Backend;

/// <summary>
/// <c>Backend:Mode=Postgres</c> (D53): queues, delivery state, rollups, archive, idempotency and quota all live in the
/// config database, so the whole product runs with PostgreSQL alone. Each role registers only what it uses.
/// </summary>
public static class PostgresBackendExtensions
{
    public static bool UsePostgresBackend(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration["Backend:Mode"], "Postgres", StringComparison.OrdinalIgnoreCase);
    }

    public static IServiceCollection AddPostgresIngest(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresShared(configuration);
        services.Replace(ServiceDescriptor.Singleton<IEventPublisher, PostgresEventPublisher>());
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore, PostgresIdempotencyStore>());
        services.Replace(ServiceDescriptor.Singleton<IQuotaCounter, PostgresQuotaCounter>());
        return services;
    }

    public static IServiceCollection AddPostgresFanOut(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresShared(configuration);
        services.TryAddSingleton<PostgresDeliveryPublisher>();
        services.Replace(ServiceDescriptor.Singleton<IEventSource, PostgresEventSource>());
        services.Replace(ServiceDescriptor.Singleton<IDeliveryPublisher>(sp => sp.GetRequiredService<PostgresDeliveryPublisher>()));
        services.Replace(ServiceDescriptor.Singleton<IEventArchive, PostgresEventArchive>());
        services.Replace(ServiceDescriptor.Singleton<IRollupWriter, PostgresRollupWriter>());
        return services;
    }

    public static IServiceCollection AddPostgresDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresShared(configuration);
        services.TryAddSingleton<PostgresDeliveryPublisher>();
        services.TryAddSingleton<PostgresDeliveryStateStore>();
        services.Replace(ServiceDescriptor.Singleton<IDeliverySource, PostgresDeliverySource>());
        services.Replace(ServiceDescriptor.Singleton<IRetryPublisher>(sp => sp.GetRequiredService<PostgresDeliveryPublisher>()));
        services.Replace(ServiceDescriptor.Singleton<IPayloadResolver, PostgresPayloadResolver>());
        services.Replace(ServiceDescriptor.Singleton<IDeliveryStateStore>(sp => sp.GetRequiredService<PostgresDeliveryStateStore>()));
        services.Replace(ServiceDescriptor.Singleton<IDueDeliveryIndex>(sp => sp.GetRequiredService<PostgresDeliveryStateStore>()));
        services.Replace(ServiceDescriptor.Singleton<IRollupWriter, PostgresRollupWriter>());
        services.AddHostedService<PostgresMaintenance>();
        return services;
    }

    private static void AddPostgresShared(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresDataSource(configuration);
        services.Configure<BackendOptions>(configuration.GetSection(BackendOptions.SectionName));
        services.TryAddSingleton<PostgresMessageStore>();
    }
}
