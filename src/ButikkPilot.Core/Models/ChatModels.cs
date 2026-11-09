using ButikkPilot.Contracts;

namespace ButikkPilot.Core.Models;

public sealed class ToolInvocation
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ArgumentsJson { get; init; } = "{}";
}

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ToolCallId { get; init; }
    public IReadOnlyList<ToolInvocation>? ToolCalls { get; init; }
}

/// <summary>
/// Server-side conversation state. Not thread-safe by itself: callers must hold <see cref="Gate"/>
/// for the duration of a turn (the conversation service does).
/// </summary>
public sealed class ChatSession
{
    public ChatSession(Guid id, DateTimeOffset createdUtc)
    {
        Id = id;
        CreatedUtc = createdUtc;
        LastAccessUtc = createdUtc;
    }

    public Guid Id { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset LastAccessUtc { get; set; }

    /// <summary>Serializes turns for one session so concurrent requests cannot interleave history.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public string Language { get; set; } = SupportedLanguages.Default;
    public int TurnCount { get; set; }

    public List<ChatMessage> History { get; } = [];

    // Visible UI state that persists across turns.
    public List<string> LastRecommendedProductIds { get; set; } = [];
    public LocationInfo? LastLocation { get; set; }
    public GuideResult? LastGuide { get; set; }

    // Per-turn output, reset at the start of every turn.
    public string? TurnHandoffTicket { get; set; }

    public void ResetTurn() => TurnHandoffTicket = null;
}
