using System.Net;
using System.Text;
using System.Text.Json;
using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using ButikkPilot.Infrastructure.Llm;
using ButikkPilot.Infrastructure.Sessions;
using ButikkPilot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ButikkPilot.Tests;

public class SessionStoreTests
{
    private static MemorySessionStore Store(ManualTime time, int max = 5, int idleMinutes = 30) =>
        new(Options.Create(new SessionOptions { MaxSessions = max, IdleMinutes = idleMinutes, MaxLifetimeHours = 4 }), time);

    [Fact]
    public void Known_ids_are_reused_unknown_ids_get_a_server_minted_one()
    {
        var store = Store(new ManualTime());
        var a = store.GetOrCreate(null);

        Assert.Same(a, store.GetOrCreate(a.Id));

        var attackerChosen = Guid.NewGuid();
        var other = store.GetOrCreate(attackerChosen);
        Assert.NotEqual(attackerChosen, other.Id);
    }

    [Fact]
    public void Idle_sessions_expire()
    {
        var time = new ManualTime();
        var store = Store(time);
        var a = store.GetOrCreate(null);

        time.Advance(TimeSpan.FromMinutes(31));

        Assert.NotEqual(a.Id, store.GetOrCreate(a.Id).Id);
    }

    [Fact]
    public void Active_sessions_survive_until_the_absolute_lifetime()
    {
        var time = new ManualTime();
        var store = Store(time);
        var a = store.GetOrCreate(null);

        for (var i = 0; i < 7; i++)
        {
            time.Advance(TimeSpan.FromMinutes(29));
            Assert.Same(a, store.GetOrCreate(a.Id));
        }

        time.Advance(TimeSpan.FromHours(1)); // now well past 4 h in total
        Assert.NotEqual(a.Id, store.GetOrCreate(a.Id).Id);
    }

    [Fact]
    public void Memory_is_bounded_even_if_a_client_spams_new_sessions()
    {
        var store = Store(new ManualTime(), max: 20);

        for (var i = 0; i < 5000; i++)
        {
            store.GetOrCreate(null);
        }

        Assert.True(store.Count <= 20, $"store grew to {store.Count}");
    }

    [Fact]
    public void Least_recently_used_sessions_are_evicted_first()
    {
        var time = new ManualTime();
        var store = Store(time, max: 10);
        var oldest = store.GetOrCreate(null);
        for (var i = 0; i < 9; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            store.GetOrCreate(null);
        }

        time.Advance(TimeSpan.FromSeconds(1));
        store.GetOrCreate(null); // overflows -> evicts the oldest

        Assert.NotEqual(oldest.Id, store.GetOrCreate(oldest.Id).Id);
    }
}

public class BudgetedLlmClientTests
{
    private static LlmCompletionResult Ok => new() { AssistantContent = "ok" };

    private static LlmRequest Req => new() { Language = "en-US", Messages = [], Tools = [] };

    [Fact]
    public async Task Calls_over_the_daily_limit_are_refused_without_hitting_the_provider()
    {
        var inner = new ScriptedLlm(_ => Ok);
        var llm = new BudgetedLlmClient(inner, Options.Create(new LlmOptions { DailyRequestLimit = 3 }), new ManualTime());

        for (var i = 0; i < 3; i++)
        {
            await llm.CompleteAsync(Req);
        }

        await Assert.ThrowsAsync<LlmBudgetExceededException>(() => llm.CompleteAsync(Req));
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task Budget_resets_on_the_next_utc_day()
    {
        var time = new ManualTime();
        var llm = new BudgetedLlmClient(new ScriptedLlm(_ => Ok), Options.Create(new LlmOptions { DailyRequestLimit = 1 }), time);
        await llm.CompleteAsync(Req);
        await Assert.ThrowsAsync<LlmBudgetExceededException>(() => llm.CompleteAsync(Req));

        time.Advance(TimeSpan.FromHours(13));
        await llm.CompleteAsync(Req);

        Assert.Equal(1, llm.UsedToday);
    }

    [Fact]
    public async Task Zero_means_unlimited()
    {
        var llm = new BudgetedLlmClient(new ScriptedLlm(_ => Ok), Options.Create(new LlmOptions { DailyRequestLimit = 0 }), new ManualTime());

        for (var i = 0; i < 50; i++)
        {
            await llm.CompleteAsync(Req);
        }
    }
}

public class LlmOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(LlmOptions o) => new LlmOptionsValidator().Validate(null, o);

    [Fact]
    public void Stub_needs_nothing() => Assert.True(Validate(new LlmOptions()).Succeeded);

    [Theory]
    [InlineData(LlmProvider.OpenAI)]
    [InlineData(LlmProvider.AzureOpenAI)]
    public void Real_providers_refuse_to_start_without_a_key(LlmProvider provider)
    {
        var result = Validate(new LlmOptions { Provider = provider, Endpoint = "https://x.openai.azure.com" });

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", string.Join(' ', result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_requires_an_https_endpoint()
    {
        Assert.True(Validate(new LlmOptions { Provider = LlmProvider.AzureOpenAI, ApiKey = "k" }).Failed);
        Assert.True(Validate(new LlmOptions { Provider = LlmProvider.AzureOpenAI, ApiKey = "k", Endpoint = "http://insecure.example" }).Failed);
        Assert.True(Validate(new LlmOptions { Provider = LlmProvider.AzureOpenAI, ApiKey = "k", Endpoint = "https://r.openai.azure.com" }).Succeeded);
    }

    [Fact]
    public void Cost_controls_are_range_checked()
    {
        Assert.True(Validate(new LlmOptions { MaxCompletionTokens = 100_000 }).Failed);
        Assert.True(Validate(new LlmOptions { AttemptTimeoutSeconds = 0 }).Failed);
        Assert.True(Validate(new LlmOptions { DailyRequestLimit = -1 }).Failed);
    }

    [Fact]
    public void An_invalid_configuration_fails_when_the_options_are_first_read_not_silently_falling_back_to_stub()
    {
        using var sp = TestServices.Build(settings: new Dictionary<string, string?> { ["Llm:Provider"] = "OpenAI" });

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<LlmOptions>>().Value);
    }
}

public class OpenAiLlmClientTests
{
    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static OpenAiLlmClient Client(CapturingHandler handler, LlmOptions options) =>
        new(new Factory(handler), Options.Create(options), NullLogger<OpenAiLlmClient>.Instance);

    private static LlmRequest Request() => new()
    {
        Language = "en-US",
        Messages =
        [
            new ChatMessage { Role = "system", Content = "sys" },
            new ChatMessage { Role = "user", Content = "hi" },
            new ChatMessage { Role = "assistant", ToolCalls = [new ToolInvocation { Id = "c1", Name = "get_product", ArgumentsJson = "{\"id\":\"x\"}" }] },
            new ChatMessage { Role = "tool", ToolCallId = "c1", Content = "{}" },
        ],
        Tools = [new { type = "function", function = new { name = "get_product" } }],
    };

    [Fact]
    public async Task OpenAi_requests_use_bearer_auth_model_and_token_cap()
    {
        var handler = new CapturingHandler(_ => Json("{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}"));
        var client = Client(handler, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "sk-test", Model = "m1", MaxCompletionTokens = 321 });

        var result = await client.CompleteAsync(Request());

        Assert.Equal("hello", result.AssistantContent);
        var (req, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", req.Headers.Authorization.Parameter);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("m1", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(321, doc.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("auto", doc.RootElement.GetProperty("tool_choice").GetString());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal("c1", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("c1", messages[3].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task Azure_requests_use_the_deployment_url_api_version_and_api_key_header()
    {
        var handler = new CapturingHandler(_ => Json("{\"choices\":[{\"message\":{\"content\":\"hi\"}}]}"));
        var client = Client(handler, new LlmOptions
        {
            Provider = LlmProvider.AzureOpenAI,
            ApiKey = "az-key",
            Endpoint = "https://res.openai.azure.com/",
            Model = "my-deploy",
            ApiVersion = "2024-10-21",
        });

        await client.CompleteAsync(Request());

        var (req, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://res.openai.azure.com/openai/deployments/my-deploy/chat/completions?api-version=2024-10-21", req.RequestUri!.ToString());
        Assert.Equal("az-key", req.Headers.GetValues("api-key").Single());
        Assert.Null(req.Headers.Authorization);
        Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task Tool_calls_are_parsed_and_missing_ids_are_synthesized()
    {
        var handler = new CapturingHandler(_ => Json("""
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"abc","type":"function","function":{"name":"search_products","arguments":"{\"category\":\"laptop\"}"}},
              {"type":"function","function":{"name":"get_product","arguments":"{}"}},
              {"type":"function","function":{"arguments":"{}"}}
            ]}}]}
            """));
        var client = Client(handler, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "k" });

        var result = await client.CompleteAsync(Request());

        Assert.Equal(2, result.ToolCalls.Count); // the nameless call is dropped
        Assert.Equal("abc", result.ToolCalls[0].Id);
        Assert.StartsWith("call_", result.ToolCalls[1].Id, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Provider_http_errors_become_LlmUnavailable(HttpStatusCode status)
    {
        var handler = new CapturingHandler(_ => Json("{\"error\":{\"code\":\"invalid_api_key\",\"message\":\"sk-leaked-in-message\"}}", status));
        var client = Client(handler, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "k" });

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Request()));

        Assert.DoesNotContain("sk-leaked", ex.Message, StringComparison.Ordinal); // provider bodies are never surfaced
    }

    [Fact]
    public async Task Garbage_success_bodies_become_LlmUnavailable()
    {
        var handler = new CapturingHandler(_ => Json("{\"choices\":[]}"));
        var client = Client(handler, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "k" });

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Request()));
    }

    [Fact]
    public async Task Timeouts_become_LlmUnavailable_but_caller_cancellation_stays_cancellation()
    {
        var timeout = new CapturingHandler(_ => throw new TaskCanceledException("http timeout"));
        var timeoutClient = Client(timeout, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "k" });
        await Assert.ThrowsAsync<LlmUnavailableException>(() => timeoutClient.CompleteAsync(Request()));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelled = new CapturingHandler(_ => throw new TaskCanceledException("cancelled"));
        var cancelledClient = Client(cancelled, new LlmOptions { Provider = LlmProvider.OpenAI, ApiKey = "k" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledClient.CompleteAsync(Request(), cts.Token));
    }
}
