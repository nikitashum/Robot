namespace ButikkPilot.Api.Hosting;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    /// <summary>Chat requests allowed per client per minute (sliding window).</summary>
    public int ChatPerMinute { get; set; } = 20;

    /// <summary>Chat requests processed at the same time across all clients (protects LLM spend and latency).</summary>
    public int MaxConcurrentChats { get; set; } = 16;
}
