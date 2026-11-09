using System.Text.Json;
using ButikkPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ButikkPilot.Infrastructure.Analytics;

/// <summary>
/// Writes anonymous product-usage events to the application log (stdout in containers, picked up by the
/// platform's log pipeline). Replaces the previous unbounded local file. Never receives customer free text.
/// </summary>
public sealed class LoggerAnalytics : IAnalytics
{
    private readonly ILogger<LoggerAnalytics> _logger;

    public LoggerAnalytics(ILogger<LoggerAnalytics> logger) => _logger = logger;

    public void Track(string eventName, object? payload = null)
    {
        try
        {
            var data = payload is null ? "{}" : JsonSerializer.Serialize(payload);
            _logger.LogInformation("analytics {Event} {Data}", eventName, data);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Could not serialize analytics payload for {Event}", eventName);
        }
    }
}
