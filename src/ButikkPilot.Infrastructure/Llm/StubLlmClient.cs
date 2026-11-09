using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using ButikkPilot.Core.Services;

namespace ButikkPilot.Infrastructure.Llm;

/// <summary>
/// Deterministic offline assistant that speaks the tool protocol like a real model would. It exists so the demo
/// and the test-suite run without network or API key, NOT to be smart. Rules are keyword based and documented here:
///  - handoff / guide / location are recognised from the last message (guide and location target the product the
///    user mentions, else the first product currently on screen, taken from the context system message);
///  - laptop/monitor intent is taken from the latest message that mentions one;
///  - the budget is parsed from the messages (latest wins) and respected in the catalog query.
/// </summary>
public sealed partial class StubLlmClient : ILLMClient
{
    private const decimal DefaultLaptopBudget = 10_000;
    private const decimal DefaultMonitorBudget = 5_000;

    public Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lang = request.Language;
        var messages = request.Messages;

        var lastUserIndex = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "user")
            {
                lastUserIndex = i;
                break;
            }
        }

        var userTexts = messages.Where(m => m.Role == "user").Select(m => m.Content).ToList();
        var lastUser = userTexts.LastOrDefault() ?? string.Empty;
        var turn = lastUserIndex >= 0 ? messages.Skip(lastUserIndex + 1).ToList() : [];
        var results = CollectToolResults(turn);

        var completion = results.Count > 0
            ? Summarize(lang, userTexts, results)
            : Plan(lang, messages, userTexts, lastUser);

        return Task.FromResult(completion);
    }

    // ---- Planning: decide the first tool call (or answer directly) --------------------------------

    private static LlmCompletionResult Plan(string lang, IReadOnlyList<ChatMessage> messages, List<string> userTexts, string lastUser)
    {
        if (HandoffRegex().IsMatch(lastUser))
        {
            return Call(ToolNames.RequestHandoff, new { reason = "customer asked for a person" });
        }

        var productIntent = DetectProductIntent(userTexts, out var mentionedInLast);

        var wantsGuide = GuideRegex().IsMatch(lastUser);
        if (wantsGuide || LocationRegex().IsMatch(lastUser))
        {
            var target = ResolveTargetProduct(messages, lastUser);
            if (target is not null)
            {
                return Call(wantsGuide ? ToolNames.SimulateGuide : ToolNames.GetLocation, new { productId = target });
            }

            // Nothing on screen to point at. Fall through if the user also asked for a product ("where do I find gaming laptops").
            if (!(productIntent != ProductIntent.None && mentionedInLast))
            {
                return Say(StubLocale.NeedProductFirst(lang));
            }
        }

        if (productIntent == ProductIntent.Monitor)
        {
            var budget = ParseBudget(userTexts) ?? DefaultMonitorBudget;
            return Call(ToolNames.SearchProducts, new { category = "monitor", tags = new[] { "gaming" }, maxPriceNok = budget, inStockOnly = true });
        }

        if (productIntent == ProductIntent.Laptop)
        {
            var budget = ParseBudget(userTexts);
            var isFollowUpAnswer = userTexts.Count == 2 && !mentionedInLast;
            var enoughInfo = UsageRegex().IsMatch(string.Join(' ', userTexts)) || isFollowUpAnswer;

            if (!enoughInfo)
            {
                return Say(StubLocale.AskLaptopDetails(lang, budget));
            }

            if (mentionedInLast || isFollowUpAnswer)
            {
                return Call(ToolNames.SearchProducts, new
                {
                    category = "laptop",
                    tags = new[] { "gaming" },
                    maxPriceNok = budget ?? DefaultLaptopBudget,
                    inStockOnly = true,
                });
            }
        }

        return Say(userTexts.Count <= 1 ? StubLocale.Help(lang) : StubLocale.AnythingElse(lang));
    }

    // ---- Summarising: turn tool results into the final answer ---------------------------------------

    private static LlmCompletionResult Summarize(string lang, List<string> userTexts, List<(string Name, string Json)> results)
    {
        var (name, json) = results[^1];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out _))
            {
                return Say(StubLocale.ToolFailed(lang));
            }

            switch (name)
            {
                case ToolNames.SearchProducts:
                    return SummarizeSearch(lang, userTexts, root);
                case ToolNames.CompareProducts:
                    return SummarizeCompare(lang, userTexts, root);
                case ToolNames.GetLocation:
                    return Say(StubLocale.Location(
                        lang,
                        Str(root, "shelfLabel"),
                        Str(root, "aisleLabel"),
                        Str(root, "departmentName"),
                        Str(root, "directions")));
                case ToolNames.SimulateGuide:
                    var steps = root.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array ? s : default;
                    var shelf = steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() > 0 ? Str(steps[steps.GetArrayLength() - 1], "label") : string.Empty;
                    var seconds = root.TryGetProperty("estimatedSeconds", out var e) && e.TryGetInt32(out var sec) ? sec : 60;
                    return Say(StubLocale.Guide(lang, seconds, shelf));
                case ToolNames.RequestHandoff:
                    return Say(StubLocale.Handoff(lang));
                default:
                    return Say(StubLocale.AnythingElse(lang));
            }
        }
        catch (JsonException)
        {
            return Say(StubLocale.ToolFailed(lang));
        }
    }

    private static LlmCompletionResult SummarizeSearch(string lang, List<string> userTexts, JsonElement results)
    {
        var isMonitor = DetectProductIntent(userTexts, out _) == ProductIntent.Monitor;
        var budget = ParseBudget(userTexts) ?? (isMonitor ? DefaultMonitorBudget : DefaultLaptopBudget);
        var products = results.ValueKind == JsonValueKind.Array ? results.EnumerateArray().Take(3).ToList() : [];

        if (products.Count == 0)
        {
            return Say(StubLocale.NoMatch(lang, budget));
        }

        if (isMonitor)
        {
            return Say(StubLocale.MonitorPicks(lang, budget, products.Select(p => Line(p, "Resolution", "RefreshRate")).ToList()));
        }

        if (products.Count >= 2)
        {
            // Mimic a real model: search first, then compare the best options.
            var ids = products.Select(p => Str(p, "id")).ToArray();
            return Call(ToolNames.CompareProducts, new { ids });
        }

        return Say(StubLocale.LaptopPicks(lang, budget, products.Select(p => Line(p, "GPU", "RAM")).ToList()));
    }

    private static LlmCompletionResult SummarizeCompare(string lang, List<string> userTexts, JsonElement root)
    {
        var budget = ParseBudget(userTexts) ?? DefaultLaptopBudget;
        var products = root.TryGetProperty("products", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().ToList() : [];
        return products.Count == 0
            ? Say(StubLocale.NoMatch(lang, budget))
            : Say(StubLocale.LaptopPicks(lang, budget, products.Select(x => Line(x, "GPU", "RAM")).ToList()));
    }

    private static string Line(JsonElement product, params string[] specKeys)
    {
        var specs = product.TryGetProperty("specs", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var parts = specKeys
            .Select(k => specs.ValueKind == JsonValueKind.Object && specs.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v));
        var price = product.TryGetProperty("priceNok", out var pr) && pr.TryGetDecimal(out var d) ? StubLocale.Money(d) : "?";
        return $"• {Str(product, "name")}, {price}, {string.Join(", ", parts)}".TrimEnd(' ', ',');
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private enum ProductIntent { None, Laptop, Monitor }

    private static ProductIntent DetectProductIntent(List<string> userTexts, out bool mentionedInLast)
    {
        mentionedInLast = false;
        for (var i = userTexts.Count - 1; i >= 0; i--)
        {
            var text = userTexts[i];
            var intent = MonitorRegex().IsMatch(text) ? ProductIntent.Monitor
                : LaptopRegex().IsMatch(text) ? ProductIntent.Laptop
                : ProductIntent.None;

            if (intent != ProductIntent.None)
            {
                mentionedInLast = i == userTexts.Count - 1;
                return intent;
            }
        }

        return ProductIntent.None;
    }

    private static decimal? ParseBudget(List<string> userTexts)
    {
        for (var i = userTexts.Count - 1; i >= 0; i--)
        {
            foreach (Match m in BudgetRegex().Matches(userTexts[i]))
            {
                var digits = new string(m.Value.Where(char.IsDigit).ToArray());
                if (decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 500 and <= 200_000)
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>The product the user means: an id they typed, else the first product currently shown (context message).</summary>
    private static string? ResolveTargetProduct(IReadOnlyList<ChatMessage> messages, string lastUser)
    {
        var context = messages.LastOrDefault(m => m.Role == "system" && m.Content.StartsWith("Products currently shown", StringComparison.Ordinal));
        if (context is null)
        {
            return null;
        }

        var shown = ShownProductRegex().Matches(context.Content)
            .Select(m => (Id: m.Groups["id"].Value, Name: m.Groups["name"].Value))
            .ToList();

        // Prefer the product the user named (by id or by display name), else the first one on screen.
        var named = shown.FirstOrDefault(p =>
            lastUser.Contains(p.Id, StringComparison.OrdinalIgnoreCase)
            || lastUser.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
        return named.Id ?? shown.FirstOrDefault().Id;
    }

    private static List<(string Name, string Json)> CollectToolResults(List<ChatMessage> turn)
    {
        var nameById = turn
            .Where(m => m.ToolCalls is not null)
            .SelectMany(m => m.ToolCalls!)
            .ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);

        return turn
            .Where(m => m.Role == "tool" && m.ToolCallId is not null && nameById.ContainsKey(m.ToolCallId))
            .Select(m => (nameById[m.ToolCallId!], m.Content))
            .ToList();
    }

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static LlmCompletionResult Say(string text) => new() { AssistantContent = text };

    private static LlmCompletionResult Call(string tool, object args) => new()
    {
        ToolCalls =
        [
            new ToolInvocation
            {
                Id = $"stub_{Guid.NewGuid():N}",
                Name = tool,
                ArgumentsJson = JsonSerializer.Serialize(args),
            },
        ],
    };

    [GeneratedRegex(@"ansatt|medarbeider|snakke med (en|et)|menneske|human|staff|employee|real person|pracowni|człowiek|obsług|konsultant", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HandoffRegex();

    [GeneratedRegex(@"simuler|simulate|guide|led meg|veiled|symuluj|prowad|zaprowad|robot", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuideRegex();

    [GeneratedRegex(@"hvor (finner|står|er|ligger)|where (can i find|is|are)|show me where|vis meg hvor|gdzie|pokaż gdzie|finn den", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"monitor|gaming.?skjerm", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonitorRegex();

    [GeneratedRegex(@"laptop|bærbar|notebook|\bpc\b|datamaskin|komputer|gaming", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaptopRegex();

    [GeneratedRegex(@"spill|game\b|gry\b|gier|fortnite|cyberpunk|minecraft|valorant|gta|fc ?\d\d|skole|student|jobb|work|studi|prac", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsageRegex();

    // 10 000 / 10,000 / 10.000 / 5000 (4+ digits or grouped thousands). Plain "15" or "27" are never budgets.
    [GeneratedRegex(@"\d{1,3}(?:[ \u00A0.,]\d{3})+|\d{4,6}", RegexOptions.CultureInvariant)]
    private static partial Regex BudgetRegex();

    // Context line format: "lap-gf-66 = MSI GF66 Katana 15 (9490 NOK); next-id = ..."
    [GeneratedRegex(@"(?<id>[a-z0-9]+(?:-[a-z0-9]+)+) = (?<name>[^;(]+?) \(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShownProductRegex();
}
