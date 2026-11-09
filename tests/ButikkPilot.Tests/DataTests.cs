using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using ButikkPilot.Infrastructure.Data;
using ButikkPilot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ButikkPilot.Tests;

public class DataTests
{
    [Fact]
    public async Task Shipped_data_has_no_validation_problems()
    {
        using var sp = TestServices.Build();
        var problems = await DataValidator.ValidateAsync(
            sp.GetRequiredService<IProductCatalog>(), sp.GetRequiredService<IStoreNavigation>());

        Assert.Empty(problems);
    }

    [Fact]
    public async Task Every_product_shelf_resolves_in_every_language()
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();
        var store = sp.GetRequiredService<IStoreNavigation>();

        foreach (var product in await catalog.GetAllAsync())
        foreach (var lang in SupportedLanguages.All)
        {
            var location = await store.ResolveLocationAsync(product.ShelfLocation, lang.Code);
            Assert.NotNull(location);
            Assert.False(string.IsNullOrWhiteSpace(location.Directions));
        }
    }

    [Fact]
    public async Task Localized_labels_actually_differ_between_languages()
    {
        using var sp = TestServices.Build();
        var store = sp.GetRequiredService<IStoreNavigation>();

        var nb = await store.ResolveLocationAsync("DEPT-PC/AISLE-3/SHELF-C1", "nb-NO");
        var en = await store.ResolveLocationAsync("DEPT-PC/AISLE-3/SHELF-C1", "en-US");
        var pl = await store.ResolveLocationAsync("DEPT-PC/AISLE-3/SHELF-C1", "pl-PL");

        Assert.NotEqual(nb!.DepartmentName, en!.DepartmentName);
        Assert.NotEqual(en.DepartmentName, pl!.DepartmentName);
        Assert.StartsWith("Shelf", en.ShelfLabel, StringComparison.Ordinal);
        Assert.StartsWith("Hylle", nb.ShelfLabel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("DEPT-PC")]
    [InlineData("DEPT-PC/AISLE-3")]
    [InlineData("DEPT-PC/AISLE-3/SHELF-NOPE")]
    [InlineData("../../etc/passwd")]
    public async Task Unknown_or_malformed_locations_resolve_to_null(string shelf)
    {
        using var sp = TestServices.Build();
        var store = sp.GetRequiredService<IStoreNavigation>();

        Assert.Null(await store.ResolveLocationAsync(shelf, "en-US"));
    }

    [Fact]
    public async Task Spec_values_are_language_neutral()
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();
        var norwegianWords = new[] { " timer", "Kablet", "Avtagbar", "Innebygd", "Blekk", "Nordisk", "leilighet", "høyttaler" };

        foreach (var value in (await catalog.GetAllAsync()).SelectMany(p => p.Specs.Values))
        {
            foreach (var word in norwegianWords)
            {
                Assert.DoesNotContain(word, value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Search_filters_by_budget_stock_and_tags_and_sorts_best_first()
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();

        var results = await catalog.SearchAsync(new ProductQuery
        {
            Category = "laptop",
            MaxPriceNok = 10_000,
            Tags = ["gaming"],
        });

        Assert.NotEmpty(results);
        Assert.All(results, p =>
        {
            Assert.True(p.PriceNok <= 10_000);
            Assert.True(p.InStock);
            Assert.Contains("gaming", p.Tags);
        });
        Assert.Equal(results.OrderByDescending(p => p.PriceNok).Select(p => p.Id), results.Select(p => p.Id));
    }

    [Theory]
    [InlineData("laptops")]
    [InlineData("LAPTOP")]
    [InlineData(" laptop ")]
    public async Task Category_matching_is_forgiving_but_exact(string category)
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();

        var results = await catalog.SearchAsync(new ProductQuery { Category = category, InStockOnly = false });

        Assert.NotEmpty(results);
        Assert.All(results, p => Assert.Equal("laptop", p.Category));
    }

    [Fact]
    public async Task Partial_category_names_do_not_match()
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();

        Assert.Empty(await catalog.SearchAsync(new ProductQuery { Category = "mon", InStockOnly = false }));
    }

    [Fact]
    public async Task Tags_are_anded_and_limit_is_capped()
    {
        using var sp = TestServices.Build();
        var catalog = sp.GetRequiredService<IProductCatalog>();

        var both = await catalog.SearchAsync(new ProductQuery { Tags = ["gaming", "no-such-tag"], InStockOnly = false });
        Assert.Empty(both);

        var all = await catalog.SearchAsync(new ProductQuery { InStockOnly = false, Limit = 10_000 });
        Assert.True(all.Count <= ProductQuery.MaxLimit);
    }
}
