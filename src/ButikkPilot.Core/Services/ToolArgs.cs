using System.Globalization;
using System.Text.Json;

namespace ButikkPilot.Core.Services;

/// <summary>
/// Defensive reader for LLM-produced tool arguments. Models routinely send wrong types (numbers as strings,
/// null, missing fields). Nothing here throws; callers get null/defaults and answer the model with an error.
/// </summary>
internal sealed class ToolArgs
{
    private readonly JsonElement _root;

    private ToolArgs(JsonElement root) => _root = root;

    public static bool TryParse(string? json, out ToolArgs args)
    {
        args = new ToolArgs(default);
        if (string.IsNullOrWhiteSpace(json))
        {
            json = "{}";
        }

        if (json.Length > 16_384)
        {
            return false;
        }

        try
        {
            // Clone so the element outlives the JsonDocument.
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            args = new ToolArgs(doc.RootElement.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string? GetString(string name, int maxLength = 120)
    {
        if (!_root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    public decimal? GetDecimal(string name)
    {
        if (!_root.TryGetProperty(name, out var value))
        {
            return null;
        }

        decimal result;
        var ok = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out result),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result),
            _ => Fail(out result),
        };

        return ok && result is >= 0 and <= 10_000_000 ? result : null;
    }

    public bool GetBool(string name, bool defaultValue)
    {
        if (!_root.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public IReadOnlyList<string>? GetStringList(string name, int maxItems = 8, int maxLength = 64)
    {
        if (!_root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString()?.Trim();
            return string.IsNullOrEmpty(single) ? null : [single.Length > maxLength ? single[..maxLength] : single];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = element.GetString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            items.Add(text.Length > maxLength ? text[..maxLength] : text);
            if (items.Count >= maxItems)
            {
                break;
            }
        }

        return items;
    }

    private static bool Fail(out decimal result)
    {
        result = 0;
        return false;
    }
}
