using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Infrastructure;
using ButikkPilot.RobotAdapter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ButikkPilot.Tests.Support;

internal sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "ButikkPilot.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Scriptable LLM that records every request it receives.</summary>
internal sealed class ScriptedLlm : ILLMClient
{
    private readonly Func<LlmRequest, LlmCompletionResult> _script;

    public ScriptedLlm(Func<LlmRequest, LlmCompletionResult> script) => _script = script;

    public List<LlmRequest> Requests { get; } = [];

    public Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(_script(request));
    }

    public static ScriptedLlm Says(string text) => new(_ => new LlmCompletionResult { AssistantContent = text });
}

internal sealed class ThrowingLlm : ILLMClient
{
    private readonly Exception _exception;

    public ThrowingLlm(Exception exception) => _exception = exception;

    public Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
        Task.FromException<LlmCompletionResult>(_exception);
}

internal static class TestServices
{
    /// <summary>Real backend (catalog, store, tools, sessions) wired without a web host. Stub LLM unless replaced.</summary>
    public static ServiceProvider Build(Action<IServiceCollection>? configure = null, IDictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddButikkPilot(config);
        services.AddSingleton<IRobotNavigation, SimulationRobotAdapter>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static void ReplaceLlm(this IServiceCollection services, ILLMClient llm)
    {
        services.RemoveAll<ILLMClient>();
        services.AddSingleton(llm);
    }

    public static async Task<ChatResponse> SayAsync(
        this IConversationService chat, string message, Guid? session = null, string language = "en-US") =>
        await chat.ProcessUserMessageAsync(new ChatRequest { Message = message, SessionId = session, Language = language });
}
