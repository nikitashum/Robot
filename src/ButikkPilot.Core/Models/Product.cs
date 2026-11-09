namespace ButikkPilot.Core.Models;

public sealed class Product
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Brand { get; init; }
    public decimal PriceNok { get; init; }
    public bool InStock { get; init; }
    public required string ShelfLocation { get; init; }
    public Dictionary<string, string> Specs { get; init; } = new();
    public List<string> Tags { get; init; } = [];
    public string? CompareGroup { get; init; }
}

/// <summary>Search filter. Category/tags/brand are compared case-insensitively; tags are AND-combined.</summary>
public sealed record ProductQuery
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;

    public string? Category { get; init; }
    public string? Brand { get; init; }
    public decimal? MinPriceNok { get; init; }
    public decimal? MaxPriceNok { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool InStockOnly { get; init; } = true;
    public int Limit { get; init; } = DefaultLimit;
}
