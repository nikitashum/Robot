using ButikkPilot.Contracts;
using ButikkPilot.Core.Models;

namespace ButikkPilot.Core.Abstractions;

public interface IProductCatalog
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);
    Task<Product?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchAsync(ProductQuery query, CancellationToken ct = default);
}

public interface IStoreNavigation
{
    Task<StoreLayout> GetLayoutAsync(CancellationToken ct = default);
    Task<StoreMapDto> GetMapAsync(string language, CancellationToken ct = default);
    Task<LocationInfo?> ResolveLocationAsync(string shelfLocation, string language, CancellationToken ct = default);
}

public interface IAnalytics
{
    /// <summary>Fire-and-forget, must never throw. Payloads must not contain customer free text or personal data.</summary>
    void Track(string eventName, object? payload = null);
}

/// <summary>The hardware seam. The simulation adapter is replaced by a real robot adapter (e.g. Pudu) later.</summary>
public interface IRobotNavigation
{
    Task<GuideResult> GuideCustomerToAsync(LocationInfo location, CancellationToken ct = default);
}

public interface ISessionStore
{
    /// <summary>Returns the session for <paramref name="requestedId"/> if it exists and is alive, otherwise a fresh one.</summary>
    ChatSession GetOrCreate(Guid? requestedId);
}

public interface IToolOrchestrator
{
    Task<string> ExecuteAsync(string toolName, string argumentsJson, ChatSession session, CancellationToken ct = default);
    IReadOnlyList<object> GetToolDefinitions();
}

public sealed class LlmRequest
{
    public required string Language { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<object> Tools { get; init; }
}

public sealed class LlmCompletionResult
{
    public string? AssistantContent { get; init; }
    public IReadOnlyList<ToolInvocation> ToolCalls { get; init; } = [];
}

public interface ILLMClient
{
    /// <exception cref="LlmUnavailableException">Provider down, timed out, rejected the call, or budget exhausted.</exception>
    Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public interface IConversationService
{
    Task<ChatResponse> ProcessUserMessageAsync(ChatRequest request, CancellationToken ct = default);
}

/// <summary>The AI backend could not produce an answer. Callers show a friendly fallback instead of failing.</summary>
public class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class LlmBudgetExceededException : LlmUnavailableException
{
    public LlmBudgetExceededException() : base("Daily LLM request budget exhausted.") { }
}
