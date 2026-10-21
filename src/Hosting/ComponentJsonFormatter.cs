using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace WebhookGateway.Hosting;

public sealed class ComponentFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>The service that wrote the line: Ingest, FanOut, Dispatcher, ControlPlane.</summary>
    public string Component { get; set; } = "Unknown";
}

/// <summary>
/// One flat JSON object per line: <c>component</c>, <c>level</c>, <c>category</c>, <c>message</c>, trace ids, the
/// structured fields of the message template (<c>tenantId</c>, <c>deliveryId</c>, ...) at the top level, and the
/// exception when there is one (docs/logging.md). Flat fields are what CloudWatch Logs Insights filters on.
/// </summary>
public sealed class ComponentJsonFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "component-json";

    private readonly IDisposable? _optionsReloadToken;
    private ComponentFormatterOptions _options;

    public ComponentJsonFormatter(IOptionsMonitor<ComponentFormatterOptions> options)
        : base(FormatterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(o => _options = o);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("component", _options.Component);
            json.WriteString("category", logEntry.Category);
            json.WriteString("message", message);

            var activity = System.Diagnostics.Activity.Current;
            if (activity is not null)
            {
                json.WriteString("traceId", activity.TraceId.ToString());
                json.WriteString("spanId", activity.SpanId.ToString());
            }

            WriteFields(json, logEntry.State);
            scopeProvider?.ForEachScope(static (scope, writer) => WriteFields(writer, scope), json);

            if (logEntry.Exception is { } ex)
            {
                json.WriteString("exceptionType", ex.GetType().FullName);
                json.WriteString("exception", ex.ToString());
            }

            json.WriteEndObject();
        }

        textWriter.WriteLine(System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length));
    }

    public void Dispose() => _optionsReloadToken?.Dispose();

    private static void WriteFields(Utf8JsonWriter json, object? state)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> fields)
        {
            return;
        }

        foreach (var (name, value) in fields)
        {
            if (name == "{OriginalFormat}")
            {
                continue;
            }

            json.WriteString(ToCamelCase(name), Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
