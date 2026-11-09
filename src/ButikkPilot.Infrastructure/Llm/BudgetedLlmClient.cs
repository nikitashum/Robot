using ButikkPilot.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace ButikkPilot.Infrastructure.Llm;

/// <summary>
/// Decorator enforcing a global daily call cap, the cost circuit-breaker for a public demo URL:
/// no matter who hammers the endpoint, spend is bounded by (limit x tokens per call).
/// </summary>
public sealed class BudgetedLlmClient : ILLMClient
{
    private readonly ILLMClient _inner;
    private readonly int _dailyLimit;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private DateOnly _day;
    private int _used;

    public BudgetedLlmClient(ILLMClient inner, IOptions<LlmOptions> options, TimeProvider time)
    {
        _inner = inner;
        _dailyLimit = options.Value.DailyRequestLimit;
        _time = time;
        _day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    }

    public int UsedToday
    {
        get
        {
            lock (_lock)
            {
                RollDayIfNeeded();
                return _used;
            }
        }
    }

    public Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (_dailyLimit > 0)
        {
            lock (_lock)
            {
                RollDayIfNeeded();
                if (_used >= _dailyLimit)
                {
                    throw new LlmBudgetExceededException();
                }

                _used++;
            }
        }

        return _inner.CompleteAsync(request, ct);
    }

    private void RollDayIfNeeded()
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _used = 0;
        }
    }
}
