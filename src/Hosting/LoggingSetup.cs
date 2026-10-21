using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace WebhookGateway.Hosting;

/// <summary>Shared logging setup for every service (docs/logging.md): one flat JSON line per event on stdout.</summary>
public static class LoggingSetup
{
    /// <param name="component">Which service writes the lines: Ingest, FanOut, Dispatcher, ControlPlane.</param>
    public static ILoggingBuilder AddStructuredJsonConsole(this ILoggingBuilder logging, string component)
    {
        ArgumentNullException.ThrowIfNull(logging);
        logging.ClearProviders();
        logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        logging.AddConsole(options => options.FormatterName = ComponentJsonFormatter.FormatterName);
        logging.AddConsoleFormatter<ComponentJsonFormatter, ComponentFormatterOptions>(options => options.Component = component);
        return logging;
    }
}
