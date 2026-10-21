using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookGateway.Hosting;

namespace WebhookGateway.UnitTests.Hosting;

public sealed class ComponentJsonFormatterTests
{
    private static JsonElement Write(string component, string message, Dictionary<string, object?> fields, Exception? exception = null)
    {
        var options = new TestOptionsMonitor(new ComponentFormatterOptions { Component = component });
        using var formatter = new ComponentJsonFormatter(options);
        using var writer = new StringWriter();
        var state = fields.Select(f => new KeyValuePair<string, object?>(f.Key, f.Value)).Append(new("{OriginalFormat}", message)).ToList();
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(LogLevel.Warning, "Some.Category", default, state, exception, (_, _) => message);
        formatter.Write(in entry, null, writer);
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    [Fact]
    public void Every_line_names_the_component_and_flattens_the_structured_fields()
    {
        var line = Write("Dispatcher", "Delivery dlv_1 of tenant ten_1 failed", new() { ["DeliveryId"] = "dlv_1", ["TenantId"] = "ten_1" });

        Assert.Equal("Dispatcher", line.GetProperty("component").GetString());
        Assert.Equal("Warning", line.GetProperty("level").GetString());
        Assert.Equal("dlv_1", line.GetProperty("deliveryId").GetString());
        Assert.Equal("ten_1", line.GetProperty("tenantId").GetString());
        Assert.Equal("Delivery dlv_1 of tenant ten_1 failed", line.GetProperty("message").GetString());
        Assert.False(line.TryGetProperty("{OriginalFormat}", out _));
    }

    [Fact]
    public void An_exception_is_included()
    {
        var line = Write("Ingest", "Publish failed", [], new InvalidOperationException("queue down"));

        Assert.Equal("System.InvalidOperationException", line.GetProperty("exceptionType").GetString());
        Assert.Contains("queue down", line.GetProperty("exception").GetString(), StringComparison.Ordinal);
    }

    private sealed class TestOptionsMonitor(ComponentFormatterOptions value) : IOptionsMonitor<ComponentFormatterOptions>
    {
        public ComponentFormatterOptions CurrentValue => value;

        public ComponentFormatterOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ComponentFormatterOptions, string?> listener) => null;
    }
}
