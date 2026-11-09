using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using Microsoft.Extensions.Options;

namespace ButikkPilot.Infrastructure.Sessions;

public sealed class SessionOptions
{
    public const string Section = "Sessions";

    public int MaxSessions { get; set; } = 500;
    public int IdleMinutes { get; set; } = 30;
    public int MaxLifetimeHours { get; set; } = 4;
}

/// <summary>
/// Bounded in-memory sessions with idle + absolute expiry and LRU eviction, so a client that spams new
/// sessions cannot grow memory without limit. Single-instance by design; use a distributed store to scale out.
/// </summary>
public sealed class MemorySessionStore : ISessionStore
{
    private readonly Dictionary<Guid, ChatSession> _sessions = [];
    private readonly Lock _lock = new();
    private readonly SessionOptions _options;
    private readonly TimeProvider _time;

    public MemorySessionStore(IOptions<SessionOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    public ChatSession GetOrCreate(Guid? requestedId)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (requestedId is { } id && _sessions.TryGetValue(id, out var existing) && !IsExpired(existing, now))
            {
                existing.LastAccessUtc = now;
                return existing;
            }

            if (_sessions.Count >= _options.MaxSessions)
            {
                EvictExpired(now);
                if (_sessions.Count >= _options.MaxSessions)
                {
                    EvictLeastRecentlyUsed(Math.Max(1, _options.MaxSessions / 10));
                }
            }

            // Always mint the id server-side: a client can never choose or fixate a session id.
            var session = new ChatSession(Guid.NewGuid(), now);
            _sessions[session.Id] = session;
            return session;
        }
    }

    private bool IsExpired(ChatSession s, DateTimeOffset now) =>
        now - s.LastAccessUtc > TimeSpan.FromMinutes(_options.IdleMinutes)
        || now - s.CreatedUtc > TimeSpan.FromHours(_options.MaxLifetimeHours);

    private void EvictExpired(DateTimeOffset now)
    {
        foreach (var key in _sessions.Where(kv => IsExpired(kv.Value, now)).Select(kv => kv.Key).ToList())
        {
            _sessions.Remove(key);
        }
    }

    private void EvictLeastRecentlyUsed(int count)
    {
        foreach (var key in _sessions.OrderBy(kv => kv.Value.LastAccessUtc).Take(count).Select(kv => kv.Key).ToList())
        {
            _sessions.Remove(key);
        }
    }
}
