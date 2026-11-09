using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ButikkPilot.Infrastructure.Data;

/// <summary>
/// Validates catalog + store layout. Runs at startup so bad data fails the deployment immediately
/// instead of surfacing as a wrong answer in front of a customer.
/// </summary>
public static class DataValidator
{
    public static async Task<IReadOnlyList<string>> ValidateAsync(
        IProductCatalog catalog, IStoreNavigation store, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var products = await catalog.GetAllAsync(ct);
        var layout = await store.GetLayoutAsync(ct);

        if (products.Count == 0)
        {
            problems.Add("Catalog is empty.");
        }

        foreach (var dup in products.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"Duplicate product id '{dup.Key}'.");
        }

        foreach (var p in products)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name))
            {
                problems.Add($"Product with empty id or name: '{p.Id}'/'{p.Name}'.");
            }

            if (p.PriceNok <= 0)
            {
                problems.Add($"Product '{p.Id}' has non-positive price {p.PriceNok}.");
            }

            if (await store.ResolveLocationAsync(p.ShelfLocation, SupportedLanguages.Default, ct) is null)
            {
                problems.Add($"Product '{p.Id}' points to unknown shelf '{p.ShelfLocation}'.");
            }
        }

        var seenAisles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dept in layout.Departments)
        {
            foreach (var aisle in dept.Aisles)
            {
                if (!seenAisles.Add($"{dept.Id}/{aisle.Id}"))
                {
                    problems.Add($"Duplicate aisle '{dept.Id}/{aisle.Id}'.");
                }

                foreach (var lang in SupportedLanguages.All)
                {
                    if (!aisle.Label.ContainsKey(lang.Code) || !aisle.Directions.ContainsKey(lang.Code))
                    {
                        problems.Add($"Aisle '{dept.Id}/{aisle.Id}' lacks label/directions for {lang.Code}.");
                    }
                }

                foreach (var dup in aisle.Shelves.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                {
                    problems.Add($"Duplicate shelf '{dup.Key}' in '{dept.Id}/{aisle.Id}'.");
                }
            }

            foreach (var lang in SupportedLanguages.All)
            {
                if (!dept.Name.ContainsKey(lang.Code))
                {
                    problems.Add($"Department '{dept.Id}' lacks a name for {lang.Code}.");
                }
            }
        }

        // The same shelf id in two aisles is physically ambiguous for staff and customers.
        var shelfIds = layout.Departments.SelectMany(d => d.Aisles.SelectMany(a => a.Shelves.Select(s => (Key: $"{d.Id}/{a.Id}", s.Id))));
        foreach (var dup in shelfIds.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(x => x.Key).Distinct().Count() > 1))
        {
            problems.Add($"Shelf id '{dup.Key}' appears in several aisles: {string.Join(", ", dup.Select(x => x.Key).Distinct())}.");
        }

        return problems;
    }
}

public sealed class DataValidationHostedService : IHostedService
{
    private readonly IProductCatalog _catalog;
    private readonly IStoreNavigation _store;
    private readonly ILogger<DataValidationHostedService> _logger;

    public DataValidationHostedService(IProductCatalog catalog, IStoreNavigation store, ILogger<DataValidationHostedService> logger)
    {
        _catalog = catalog;
        _store = store;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = await DataValidator.ValidateAsync(_catalog, _store, cancellationToken);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid store data:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        var count = (await _catalog.GetAllAsync(cancellationToken)).Count;
        _logger.LogInformation("Store data validated: {Products} products", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
