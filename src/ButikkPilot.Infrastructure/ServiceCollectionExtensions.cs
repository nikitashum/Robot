using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Services;
using ButikkPilot.Infrastructure.Analytics;
using ButikkPilot.Infrastructure.Data;
using ButikkPilot.Infrastructure.Llm;
using ButikkPilot.Infrastructure.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ButikkPilot.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the whole backend except <see cref="IRobotNavigation"/> (chosen by the host: simulation or real robot).</summary>
    public static IServiceCollection AddButikkPilot(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DataOptions>().Bind(configuration.GetSection(DataOptions.Section));
        services.AddOptions<SessionOptions>().Bind(configuration.GetSection(SessionOptions.Section));
        services.AddOptions<ConversationOptions>().Bind(configuration.GetSection("Conversation"));
        services.AddOptions<LlmOptions>().Bind(configuration.GetSection(LlmOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();

        services.AddSingleton<DataPathResolver>();
        services.AddSingleton<IProductCatalog, JsonProductCatalog>();
        services.AddSingleton<IStoreNavigation, JsonStoreNavigation>();
        services.AddHostedService<DataValidationHostedService>();

        services.AddSingleton<ISessionStore, MemorySessionStore>();
        services.AddSingleton<IAnalytics, LoggerAnalytics>();
        services.AddSingleton<IToolOrchestrator, ToolOrchestrator>();
        services.AddSingleton<IConversationService, ConversationService>();

        AddLlm(services, configuration);
        return services;
    }

    private static void AddLlm(IServiceCollection services, IConfiguration configuration)
    {
        var llm = configuration.GetSection(LlmOptions.Section).Get<LlmOptions>() ?? new LlmOptions();

        if (llm.Provider == LlmProvider.Stub)
        {
            services.AddSingleton<ILLMClient, StubLlmClient>();
            return;
        }

        var attemptTimeout = TimeSpan.FromSeconds(Math.Clamp(llm.AttemptTimeoutSeconds, 3, 120));
        services.AddSingleton<OpenAiLlmClient>();
        services.AddHttpClient(OpenAiLlmClient.HttpClientName)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = attemptTimeout;
                o.TotalRequestTimeout.Timeout = attemptTimeout * 2 + TimeSpan.FromSeconds(5);
                o.Retry.MaxRetryAttempts = 1;
                // Circuit breaker rule: sampling window must be at least twice the attempt timeout.
                o.CircuitBreaker.SamplingDuration = attemptTimeout * 3;
            });

        services.AddSingleton<ILLMClient>(sp => new BudgetedLlmClient(
            sp.GetRequiredService<OpenAiLlmClient>(),
            sp.GetRequiredService<IOptions<LlmOptions>>(),
            sp.GetRequiredService<TimeProvider>()));
    }
}
