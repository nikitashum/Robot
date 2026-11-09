using System.Text.Json;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;

namespace ButikkPilot.Infrastructure.Data;

/// <summary>Read-only, in-memory catalog loaded once from products.json (swap for a real source later).</summary>
public sealed class JsonProductCatalog : IProductCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Lazy<Task<CatalogData>> _data;

    public JsonProductCatalog(DataPathResolver paths)
    {
        _data = new Lazy<Task<CatalogData>>(() => LoadAsync(paths.FilePath("products.json")));
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default) =>
        (await _data.Value).All;

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) =>
        (await _data.Value).ById.GetValueOrDefault(id);

    public async Task<IReadOnlyList<Product>> SearchAsync(ProductQuery query, CancellationToken ct = default)
    {
        var data = await _data.Value;
        IEnumerable<Product> q = data.All;

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var wanted = Singular(query.Category);
            q = q.Where(p => string.Equals(Singular(p.Category), wanted, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            q = q.Where(p => string.Equals(p.Brand, query.Brand.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (query.MinPriceNok is { } min)
        {
            q = q.Where(p => p.PriceNok >= min);
        }

        if (query.MaxPriceNok is { } max)
        {
            q = q.Where(p => p.PriceNok <= max);
        }

        if (query.Tags is { Count: > 0 } tags)
        {
            q = q.Where(p => tags.All(t => p.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        }

        if (query.InStockOnly)
        {
            q = q.Where(p => p.InStock);
        }

        var limit = Math.Clamp(query.Limit, 1, ProductQuery.MaxLimit);
        return q.OrderByDescending(p => p.PriceNok).ThenBy(p => p.Id, StringComparer.Ordinal).Take(limit).ToList();
    }

    private static string Singular(string value)
    {
        var v = value.Trim();
        return v.Length > 3 && v.EndsWith('s') ? v[..^1] : v;
    }

    private static async Task<CatalogData> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var products = await JsonSerializer.DeserializeAsync<List<Product>>(stream, JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty.");
        return new CatalogData(products);
    }

    private sealed class CatalogData
    {
        public CatalogData(List<Product> products)
        {
            All = products;
            ById = products
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<Product> All { get; }
        public Dictionary<string, Product> ById { get; }
    }
}
