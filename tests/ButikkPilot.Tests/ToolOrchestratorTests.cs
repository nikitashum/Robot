using System.Text.Json;
using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using ButikkPilot.Core.Services;
using ButikkPilot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ButikkPilot.Tests;

/// <summary>LLMs send garbage arguments all the time. None of it may throw or corrupt state.</summary>
public class ToolOrchestratorTests
{
    private static ChatSession NewSession() => new(Guid.NewGuid(), DateTimeOffset.UtcNow) { Language = "en-US" };

    private static async Task<JsonElement> RunAsync(string tool, string args, ChatSession? session = null)
    {
        using var sp = TestServices.Build();
        var tools = sp.GetRequiredService<IToolOrchestrator>();
        var json = await tools.ExecuteAsync(tool, args, session ?? NewSession());
        using var doc = JsonDocument.Parse(json); // the result must always be valid JSON
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("search_products", "not json at all")]
    [InlineData("search_products", "[1,2,3]")]
    [InlineData("search_products", "\"a string\"")]
    [InlineData("get_product", "{\"id\": 42}")]
    [InlineData("get_product", "{}")]
    [InlineData("compare_products", "{\"ids\": \"lap-gf-66\"}")]
    [InlineData("compare_products", "{\"ids\": [1, 2]}")]
    [InlineData("compare_products", "{\"ids\": null}")]
    [InlineData("get_location", "{}")]
    [InlineData("get_location", "{\"productId\": null}")]
    [InlineData("simulate_guide_to_location", "{\"shelfLocation\": {\"x\": 1}}")]
    [InlineData("no_such_tool", "{}")]
    public async Task Malformed_arguments_return_an_error_object_and_never_throw(string tool, string args)
    {
        var result = await RunAsync(tool, args);

        Assert.True(result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out _),
            $"{tool}({args}) should return an error object but returned {result}");
    }

    [Fact]
    public async Task Search_tolerates_wrong_types_and_still_filters()
    {
        var result = await RunAsync("search_products",
            "{\"category\":\"laptop\",\"maxPriceNok\":\"9000\",\"tags\":\"gaming\",\"inStockOnly\":\"true\"}");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.NotEqual(0, result.GetArrayLength());
        foreach (var p in result.EnumerateArray())
        {
            Assert.True(p.GetProperty("priceNok").GetDecimal() <= 9000);
        }
    }

    [Fact]
    public async Task Search_ignores_absurd_values_instead_of_throwing()
    {
        var result = await RunAsync("search_products",
            "{\"maxPriceNok\": 1e308, \"minPriceNok\": -5, \"tags\": [" + string.Join(",", Enumerable.Range(0, 500).Select(i => "\"t" + i + "\"")) + "]}");

        Assert.Equal(JsonValueKind.Array, result.ValueKind); // empty array is fine
    }

    [Fact]
    public async Task Compare_reports_unknown_ids_so_the_model_can_recover()
    {
        var result = await RunAsync("compare_products", "{\"ids\":[\"lap-gf-66\",\"does-not-exist\"]}");

        Assert.Equal("not_found", result.GetProperty("error").GetString());
        Assert.Contains("does-not-exist", result.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_rejects_a_single_product_and_duplicates()
    {
        var one = await RunAsync("compare_products", "{\"ids\":[\"lap-gf-66\"]}");
        var dup = await RunAsync("compare_products", "{\"ids\":[\"lap-gf-66\",\"LAP-GF-66\"]}");

        Assert.True(one.TryGetProperty("error", out _));
        Assert.True(dup.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Compare_updates_the_screen_state_and_keeps_requested_order()
    {
        var session = NewSession();

        await RunAsync("compare_products", "{\"ids\":[\"lap-ideapad-gaming\",\"lap-gf-66\"]}", session);

        Assert.Equal(["lap-ideapad-gaming", "lap-gf-66"], session.LastRecommendedProductIds);
    }

    [Fact]
    public async Task Handoff_returns_a_ticket_and_stores_no_free_text()
    {
        var session = NewSession();

        var result = await RunAsync("request_employee_handoff", "{\"reason\":\"call me on 99 99 99 99\"}", session);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(result.GetProperty("ticket").GetString(), session.TurnHandoffTicket);
        Assert.DoesNotContain("99 99", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Location_and_guide_resolve_through_product_id()
    {
        var session = NewSession();

        var location = await RunAsync("get_location", "{\"productId\":\"lap-gf-66\"}", session);
        var guide = await RunAsync("simulate_guide_to_location", "{\"productId\":\"lap-gf-66\"}", session);

        Assert.Equal("DEPT-PC/AISLE-3", location.GetProperty("pointKey").GetString());
        Assert.Equal(4, guide.GetProperty("steps").GetArrayLength());
        Assert.NotNull(session.LastGuide);
        Assert.NotNull(session.LastLocation);
    }

    [Fact]
    public async Task Shelf_path_traversal_attempts_are_just_unknown_locations()
    {
        var result = await RunAsync("get_location", "{\"shelfLocation\":\"../../etc/passwd\"}");

        Assert.Equal("not_found", result.GetProperty("error").GetString());
    }

    [Fact]
    public void Tool_definitions_are_valid_json_schema_shaped()
    {
        using var sp = TestServices.Build();
        var defs = sp.GetRequiredService<IToolOrchestrator>().GetToolDefinitions();

        var names = JsonSerializer.SerializeToElement(defs).EnumerateArray()
            .Select(d => d.GetProperty("function").GetProperty("name").GetString())
            .ToList();

        Assert.Equal(
            ["search_products", "get_product", "compare_products", "get_location", "request_employee_handoff", "simulate_guide_to_location"],
            names);
    }
}
