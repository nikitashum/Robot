namespace ButikkPilot.Contracts;

/// <summary>Limits shared by client (UI maxlength) and server (validation) so they cannot drift apart.</summary>
public static class ChatLimits
{
    public const int MaxMessageLength = 800;
}

public sealed class ChatRequest
{
    /// <summary>Existing session to continue. Unknown/expired ids are ignored and a new session is issued.</summary>
    public Guid? SessionId { get; init; }

    public required string Message { get; init; }

    /// <summary>BCP-47 code from <see cref="SupportedLanguages"/>. Anything else falls back to the default.</summary>
    public string? Language { get; init; }
}

public sealed class ChatResponse
{
    public required Guid SessionId { get; init; }

    /// <summary>The normalized language the assistant answered in.</summary>
    public required string Language { get; init; }

    public required string AssistantMessage { get; init; }

    public IReadOnlyList<ProductCardDto> RecommendedProducts { get; init; } = [];

    public LocationInfo? Location { get; init; }

    public GuideResult? Guide { get; init; }

    /// <summary>Set when a human-handoff was requested this turn. The UI localizes the confirmation text.</summary>
    public string? HandoffTicket { get; init; }

    /// <summary>True when the AI service was unavailable and a canned fallback message was returned.</summary>
    public bool Degraded { get; init; }
}

public sealed class ProductCardDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Brand { get; init; }
    public required decimal PriceNok { get; init; }
    public required bool InStock { get; init; }
    public required string ShelfLocation { get; init; }
    public IReadOnlyDictionary<string, string> Specs { get; init; } = new Dictionary<string, string>();
}
