using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace WebhookGateway.Valkey;

public static class ValkeyServiceCollectionExtensions
{
    public static bool UseValkey(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration[$"{ValkeyOptions.SectionName}:ConnectionString"]);
    }

    /// <summary>
    /// Registers one multiplexer. <c>AbortOnConnectFail=false</c> lets the service start while Valkey is down; commands
    /// then fail fast and the callers fall back to local behavior.
    /// </summary>
    public static IServiceCollection AddValkeyConnection(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ValkeyOptions.SectionName).Get<ValkeyOptions>() ?? new ValkeyOptions();
        var timeout = (int)options.CommandTimeout.TotalMilliseconds;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString!);
            config.AbortOnConnectFail = false;
            config.ConnectTimeout = timeout * 10;
            config.AsyncTimeout = timeout;
            config.SyncTimeout = timeout;
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddSingleton<ValkeyRateLimiter>();
        services.AddSingleton<ValkeyIdempotencyStore>();
        services.AddSingleton<ValkeyQuotaCounter>();
        return services;
    }
}
