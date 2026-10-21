using Microsoft.Extensions.Logging;

namespace WebhookGateway.Hosting;

public static partial class StartupLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Service started in {Mode} mode")]
    public static partial void LogServiceStarted(this ILogger logger, string mode);
}
