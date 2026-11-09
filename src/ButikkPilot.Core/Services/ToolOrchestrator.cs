using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;

namespace ButikkPilot.Core.Services;

public static class ToolNames
{
    public const string SearchProducts = "search_products";
    public const string GetProduct = "get_product";
    public const string CompareProducts = "compare_products";
    public const string GetLocation = "get_location";
    public const string RequestHandoff = "request_employee_handoff";
    public const string SimulateGuide = "simulate_guide_to_location";
}

/// <summary>Executes the tools the LLM may call. Every tool is read-only/simulated; none can change store data.</summary>
public sealed class ToolOrchestrator : IToolOrchestrator
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxCardsShown = 6;

    private readonly IProductCatalog _catalog;
    private readonly IStoreNavigation _store;
    private readonly IRobotNavigation _robot;
    private readonly IAnalytics _analytics;

    public ToolOrchestrator(IProductCatalog catalog, IStoreNavigation store, IRobotNavigation robot, IAnalytics analytics)
    {
        _catalog = catalog;
        _store = store;
        _robot = robot;
        _analytics = analytics;
    }

    public IReadOnlyList<object> GetToolDefinitions() => ToolDefinitions.All;

    public async Task<string> ExecuteAsync(string toolName, string argumentsJson, ChatSession session, CancellationToken ct = default)
    {
        if (!ToolArgs.TryParse(argumentsJson, out var args))
        {
            return Error("invalid_arguments", "Arguments must be a JSON object.");
        }

        return toolName switch
        {
            ToolNames.SearchProducts => await SearchProductsAsync(args, session, ct),
            ToolNames.GetProduct => await GetProductAsync(args, ct),
            ToolNames.CompareProducts => await CompareProductsAsync(args, session, ct),
            ToolNames.GetLocation => await GetLocationAsync(args, session, ct),
            ToolNames.RequestHandoff => RequestHandoff(args, session),
            ToolNames.SimulateGuide => await SimulateGuideAsync(args, session, ct),
            _ => Error("unknown_tool", $"Unknown tool: {toolName}"),
        };
    }

    private async Task<string> SearchProductsAsync(ToolArgs args, ChatSession session, CancellationToken ct)
    {
        var query = new ProductQuery
        {
            Category = args.GetString("category", 40),
            Brand = args.GetString("brand", 40),
            MinPriceNok = args.GetDecimal("minPriceNok"),
            MaxPriceNok = args.GetDecimal("maxPriceNok"),
            Tags = args.GetStringList("tags"),
            InStockOnly = args.GetBool("inStockOnly", true),
            Limit = ProductQuery.DefaultLimit,
        };

        var results = await _catalog.SearchAsync(query, ct);
        SetRecommended(session, results.Take(MaxCardsShown).Select(p => p.Id));
        _analytics.Track("products_recommended", new { count = results.Count, category = query.Category });
        return JsonSerializer.Serialize(results.Select(ToLlmProduct), Json);
    }

    private async Task<string> GetProductAsync(ToolArgs args, CancellationToken ct)
    {
        var id = args.GetString("id", 64);
        if (id is null)
        {
            return Error("missing_argument", "id is required.");
        }

        var product = await _catalog.GetByIdAsync(id, ct);
        return product is null
            ? Error("not_found", $"No product with id '{id}'.")
            : JsonSerializer.Serialize(ToLlmProduct(product), Json);
    }

    private async Task<string> CompareProductsAsync(ToolArgs args, ChatSession session, CancellationToken ct)
    {
        var ids = args.GetStringList("ids", maxItems: 6, maxLength: 64)?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids is null || ids.Count is < 2 or > 4)
        {
            return Error("invalid_argument", "Provide between 2 and 4 distinct product ids.");
        }

        var products = new List<Product>();
        var unknown = new List<string>();
        foreach (var id in ids)
        {
            var product = await _catalog.GetByIdAsync(id, ct);
            if (product is null)
            {
                unknown.Add(id);
            }
            else
            {
                products.Add(product);
            }
        }

        if (unknown.Count > 0)
        {
            return Error("not_found", $"Unknown product ids: {string.Join(", ", unknown)}. Use ids returned by search_products.");
        }

        SetRecommended(session, products.Select(p => p.Id));
        _analytics.Track("products_compared", new { count = products.Count });
        return JsonSerializer.Serialize(ProductComparison.Compare(products), Json);
    }

    private async Task<string> GetLocationAsync(ToolArgs args, ChatSession session, CancellationToken ct)
    {
        var (location, error) = await ResolveAsync(args, session, ct);
        if (location is null)
        {
            return error!;
        }

        session.LastLocation = location;
        session.LastGuide = null;
        return JsonSerializer.Serialize(location, Json);
    }

    private string RequestHandoff(ToolArgs args, ChatSession session)
    {
        // The free-text reason is intentionally NOT stored or logged: it may contain personal data.
        _ = args.GetString("reason", 200);
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        session.TurnHandoffTicket = ticket;
        _analytics.Track("handoff_requested", new { productCount = session.LastRecommendedProductIds.Count });

        // Prototype: no staff notification is actually sent. The UI labels this as a demo.
        return JsonSerializer.Serialize(new { success = true, ticket, note = "Prototype: a staff member would be alerted in a real deployment." }, Json);
    }

    private async Task<string> SimulateGuideAsync(ToolArgs args, ChatSession session, CancellationToken ct)
    {
        var (location, error) = await ResolveAsync(args, session, ct);
        if (location is null)
        {
            return error!;
        }

        var guide = await _robot.GuideCustomerToAsync(location, ct);
        session.LastLocation = location;
        session.LastGuide = guide;
        _analytics.Track("guide_simulated", new { guide.EstimatedSeconds });
        return JsonSerializer.Serialize(guide, Json);
    }

    private async Task<(Contracts.LocationInfo? Location, string? Error)> ResolveAsync(ToolArgs args, ChatSession session, CancellationToken ct)
    {
        var shelf = args.GetString("shelfLocation", 80);
        if (shelf is null)
        {
            var productId = args.GetString("productId", 64);
            if (productId is null)
            {
                return (null, Error("missing_argument", "Provide productId or shelfLocation."));
            }

            var product = await _catalog.GetByIdAsync(productId, ct);
            if (product is null)
            {
                return (null, Error("not_found", $"No product with id '{productId}'."));
            }

            shelf = product.ShelfLocation;
        }

        var location = await _store.ResolveLocationAsync(shelf, session.Language, ct);
        return location is null
            ? (null, Error("not_found", "That shelf location does not exist in this store."))
            : (location, null);
    }

    private static void SetRecommended(ChatSession session, IEnumerable<string> ids)
    {
        var list = ids.ToList();
        if (!list.SequenceEqual(session.LastRecommendedProductIds, StringComparer.Ordinal))
        {
            // A different product set invalidates the previously shown location/guide.
            session.LastLocation = null;
            session.LastGuide = null;
        }

        session.LastRecommendedProductIds = list;
    }

    private static object ToLlmProduct(Product p) => new
    {
        p.Id,
        p.Name,
        p.Brand,
        p.Category,
        p.PriceNok,
        p.InStock,
        p.ShelfLocation,
        p.Specs,
        p.Tags,
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { error = code, message }, Json);
}

public static class ProductComparison
{
    public static object Compare(IReadOnlyList<Product> products)
    {
        var specKeys = products.SelectMany(p => p.Specs.Keys).Distinct().Order().ToList();
        var rows = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.Brand,
            p.PriceNok,
            p.InStock,
            p.ShelfLocation,
            Specs = specKeys.ToDictionary(k => k, k => p.Specs.GetValueOrDefault(k, "-")),
        }).ToList();

        return new { products = rows, specKeys };
    }
}

internal static class ToolDefinitions
{
    private const string Categories =
        "laptop, desktop, monitor, keyboard, mouse, headset, earbuds, smartphone, tablet, gpu, storage, ram, webcam, network, speaker, tv, console, printer, nas";

    public static readonly IReadOnlyList<object> All =
    [
        Tool(ToolNames.SearchProducts,
            $"Search the store catalog. Results are sorted by price, highest first, max 10. Category must be one of: {Categories}. Tags are ANDed (e.g. gaming, student, premium, budget, midrange).",
            new
            {
                category = new { type = "string", description = "One of the allowed categories." },
                brand = new { type = "string" },
                minPriceNok = new { type = "number" },
                maxPriceNok = new { type = "number", description = "Customer budget ceiling in NOK." },
                tags = new { type = "array", items = new { type = "string" } },
                inStockOnly = new { type = "boolean", description = "Default true." },
            }),
        Tool(ToolNames.GetProduct, "Get one product by id.",
            new { id = new { type = "string" } }, required: ["id"]),
        Tool(ToolNames.CompareProducts, "Compare 2-4 products side by side. Use ids returned by search_products.",
            new { ids = new { type = "array", items = new { type = "string" } } }, required: ["ids"]),
        Tool(ToolNames.GetLocation, "Get the shelf, aisle and walking directions for a product.",
            new
            {
                productId = new { type = "string" },
                shelfLocation = new { type = "string", description = "Format DEPT-PC/AISLE-3/SHELF-C1" },
            }),
        Tool(ToolNames.RequestHandoff, "Ask a human store employee to take over. Use when the customer asks for a person or the question is beyond you.",
            new { reason = new { type = "string", description = "Short reason, no personal data." } }, required: ["reason"]),
        Tool(ToolNames.SimulateGuide, "Simulate the robot guiding the customer to a product (prototype, no physical robot).",
            new
            {
                productId = new { type = "string" },
                shelfLocation = new { type = "string" },
            }),
    ];

    private static object Tool(string name, string description, object properties, string[]? required = null) => new
    {
        type = "function",
        function = new
        {
            name,
            description,
            parameters = new { type = "object", properties, required = required ?? [] },
        },
    };
}
