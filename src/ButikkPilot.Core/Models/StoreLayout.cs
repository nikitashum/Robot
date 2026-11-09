namespace ButikkPilot.Core.Models;

/// <summary>Text keyed by language code ("nb-NO", "en-US", ...). Falls back to English, then any value.</summary>
public sealed class LocalizedText : Dictionary<string, string>
{
    public string Get(string language)
    {
        if (TryGetValue(language, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        if (TryGetValue("en-US", out var en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }
}

public sealed class StoreLayout
{
    public required string StoreName { get; init; }
    public List<Department> Departments { get; init; } = [];
}

public sealed class Department
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public List<Aisle> Aisles { get; init; } = [];
}

public sealed class Aisle
{
    public required string Id { get; init; }
    public required LocalizedText Label { get; init; }
    public required LocalizedText Directions { get; init; }
    public int MapX { get; init; }
    public int MapY { get; init; }
    public List<Shelf> Shelves { get; init; } = [];
}

public sealed class Shelf
{
    public required string Id { get; init; }
}
