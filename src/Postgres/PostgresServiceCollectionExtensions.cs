using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace WebhookGateway.Postgres;

public static class PostgresServiceCollectionExtensions
{
    public static bool UsePostgres(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[$"{PostgresOptions.SectionName}:ConnectionString"]);
    }

    /// <summary>One pooled data source per service. Stores are registered by the services that need them.</summary>
    public static IServiceCollection AddPostgresDataSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        var connectionString = configuration[$"{PostgresOptions.SectionName}:ConnectionString"]!;
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        return services;
    }
}
