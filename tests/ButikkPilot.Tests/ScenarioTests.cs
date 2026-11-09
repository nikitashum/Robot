using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ButikkPilot.Tests;

/// <summary>End-to-end conversations against the real tools/catalog with the offline stub assistant.</summary>
public class ScenarioTests
{
    private static IConversationService Chat(ServiceProvider sp) => sp.GetRequiredService<IConversationService>();

    [Fact]
    public async Task Hero_scenario_recommends_only_in_budget_in_stock_laptops_then_locates_and_guides()
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);

        var t1 = await chat.SayAsync("I need a gaming laptop for around 10,000 NOK.");
        Assert.Empty(t1.RecommendedProducts);                 // asks a follow-up first, like a real assistant
        Assert.Contains("?", t1.AssistantMessage, StringComparison.Ordinal);

        var t2 = await chat.SayAsync("I play Fortnite and FC 25, preferably a 15-inch screen.", t1.SessionId);
        Assert.InRange(t2.RecommendedProducts.Count, 2, 3);
        Assert.All(t2.RecommendedProducts, p =>
        {
            Assert.True(p.PriceNok <= 10_000, $"{p.Name} costs {p.PriceNok}, over the stated budget");
            Assert.True(p.InStock);
        });

        var t3 = await chat.SayAsync("Where can I find the first one?", t1.SessionId);
        Assert.NotNull(t3.Location);
        Assert.Equal(t2.RecommendedProducts[0].ShelfLocation[..t3.Location.PointKey.Length], t3.Location.PointKey);
        Assert.Null(t3.Guide);

        var t4 = await chat.SayAsync("Can the robot guide me there?", t1.SessionId);
        Assert.NotNull(t4.Guide);
        Assert.Equal(
            [GuideStepKind.Start, GuideStepKind.Department, GuideStepKind.Aisle, GuideStepKind.Arrive],
            t4.Guide.Steps.Select(s => s.Kind));
        Assert.True(t4.Guide.EstimatedSeconds > 0);

        var t5 = await chat.SayAsync("I'd like to talk to a staff member.", t1.SessionId);
        Assert.False(string.IsNullOrEmpty(t5.HandoffTicket));
    }

    [Fact]
    public async Task Budget_in_the_message_is_respected_not_hardcoded()
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);

        var first = await chat.SayAsync("I need a gaming laptop under 9000 NOK");
        var second = await chat.SayAsync("I play Minecraft", first.SessionId);

        Assert.NotEmpty(second.RecommendedProducts);
        Assert.All(second.RecommendedProducts, p => Assert.True(p.PriceNok <= 9000));
    }

    [Fact]
    public async Task Monitor_request_returns_monitors_not_laptops()
    {
        using var sp = TestServices.Build();

        var response = await Chat(sp).SayAsync("Do you have a 27-inch gaming monitor under 5000 NOK?");

        Assert.NotEmpty(response.RecommendedProducts);
        Assert.All(response.RecommendedProducts, p =>
        {
            Assert.True(p.PriceNok <= 5000);
            Assert.StartsWith("DEPT-MON", p.ShelfLocation, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Selecting_a_specific_product_by_name_locates_that_product()
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);
        var first = await chat.SayAsync("I need a gaming laptop for around 10,000 NOK.");
        var recs = await chat.SayAsync("I play Fortnite", first.SessionId);
        var target = recs.RecommendedProducts[^1];

        var located = await chat.SayAsync($"Where can I find the {target.Name}?", first.SessionId);

        Assert.NotNull(located.Location);
        Assert.StartsWith(located.Location.PointKey, target.ShelfLocation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nb-NO", "Hvor finner jeg den første?", "Hylle")]
    [InlineData("en-US", "Where can I find the first one?", "Shelf")]
    [InlineData("pl-PL", "Gdzie znajdę pierwszy?", "Półka")]
    public async Task Location_answers_come_back_in_the_selected_language(string lang, string where, string shelfWord)
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);
        var first = await chat.SayAsync("Do you have a 27-inch gaming monitor under 5000 NOK?", language: lang);

        var located = await chat.SayAsync(where, first.SessionId, lang);

        Assert.Equal(lang, located.Language);
        Assert.NotNull(located.Location);
        Assert.Contains(shelfWord, located.Location.ShelfLabel, StringComparison.Ordinal);
        Assert.Contains(located.Location.ShelfLabel, located.AssistantMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Where_with_nothing_on_screen_asks_for_a_product_instead_of_failing()
    {
        using var sp = TestServices.Build();

        var response = await Chat(sp).SayAsync("Where can I find it?");

        Assert.Null(response.Location);
        Assert.False(response.Degraded);
        Assert.False(string.IsNullOrWhiteSpace(response.AssistantMessage));
    }

    [Fact]
    public async Task Unknown_intent_gets_help_not_a_random_search()
    {
        using var sp = TestServices.Build();

        var response = await Chat(sp).SayAsync("What's the weather like?");

        Assert.Empty(response.RecommendedProducts);
    }

    [Fact]
    public async Task Sessions_do_not_leak_state_into_each_other()
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);

        var a = await chat.SayAsync("Do you have a 27-inch gaming monitor under 5000 NOK?");
        var b = await chat.SayAsync("hello");

        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.NotEmpty(a.RecommendedProducts);
        Assert.Empty(b.RecommendedProducts);
    }

    [Fact]
    public async Task A_new_search_clears_the_previous_location_and_guide()
    {
        using var sp = TestServices.Build();
        var chat = Chat(sp);
        var monitors = await chat.SayAsync("Do you have a 27-inch gaming monitor under 5000 NOK?");
        var located = await chat.SayAsync("Where can I find the first one?", monitors.SessionId);
        Assert.NotNull(located.Location);

        var laptops = await chat.SayAsync("I need a gaming laptop for 10 000 NOK and I play Fortnite", monitors.SessionId);

        Assert.NotEmpty(laptops.RecommendedProducts);
        Assert.Null(laptops.Location);
        Assert.Null(laptops.Guide);
    }
}
