using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebhookGateway.Hosting;

/// <summary>Shared logging setup for every service (docs/logging.md): one JSON line per event on stdout.</summary>
public static class LoggingSetup
{
    public static ILoggingBuilder AddStructuredJsonConsole(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        return logging;
    }
}
