namespace ButikkPilot.Web.Services;

public enum DemoState
{
    Idle,
    Running,
    Paused,
    Completed,
    Failed,
}

/// <summary>What the guided demo needs from the page. Implemented by Home; faked in tests.</summary>
public interface IGuidedDemoHost
{
    UiText Text { get; }

    /// <summary>Number of products currently recommended on screen.</summary>
    int ProductCount { get; }

    Task ResetAsync();

    /// <summary>Sends a customer message and waits for the answer. Returns false on a (displayed) failure; throws OperationCanceledException when cancelled.</summary>
    Task<bool> SendAsync(string message, CancellationToken ct);

    void PulseProducts(bool on);
}

/// <summary>
/// Scripted ~90 s walkthrough: greet, recommend, locate, robot guide. Fixes over the first version:
/// skip cancels only the current step, pause really pauses, every exit path leaves a consistent state,
/// and a finished demo can be replayed.
/// </summary>
public sealed class GuidedDemoRunner : IDisposable
{
    public const int StepCount = 4;

    private readonly object _gate = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _stepCts;
    private TaskCompletionSource _resume = NewGate();

    public DemoState State { get; private set; }

    /// <summary>1-based step currently executing; 0 when idle.</summary>
    public int CurrentStep { get; private set; }

    public bool IsActive => State is DemoState.Running or DemoState.Paused;

    /// <summary>True while the scripted customer is "about to speak" or browse — pause targets this beat, not the assistant.</summary>
    public bool AwaitingUserTurn { get; private set; }

    /// <summary>Beat before the demo posts the next customer line (typing / speaking). Tests set to zero.</summary>
    public TimeSpan BeforeUserMessageDelay { get; set; } = TimeSpan.FromSeconds(3.5);

    /// <summary>Brief pause after the assistant answers (scroll/read headline only). Tests set to zero.</summary>
    public TimeSpan AfterAssistantDelay { get; set; } = TimeSpan.FromSeconds(1.2);

    /// <summary>Step 2: customer scans the recommendation cards before the next question.</summary>
    public TimeSpan ProductShowcaseDelay { get; set; } = TimeSpan.FromSeconds(4);

    public Exception? LastError { get; private set; }

    public event Action? Changed;

    public async Task<DemoState> RunAsync(IGuidedDemoHost host, CancellationToken external = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        CancellationToken run;
        lock (_gate)
        {
            // Running/Paused: already in a demo. Completed/Failed/Idle: start (or restart) a new run.
            if (IsActive)
            {
                return State;
            }

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(external);
            run = _runCts.Token;
            _resume = NewGate();
            LastError = null;
            Set(DemoState.Running, 0);
        }

        try
        {
            await host.ResetAsync();

            for (var step = 1; step <= StepCount; step++)
            {
                run.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(run);

                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(run);
                lock (_gate)
                {
                    _stepCts = stepCts;
                    Set(State, step);
                }

                try
                {
                    await RunStepAsync(step, host, stepCts.Token);
                }
                catch (OperationCanceledException) when (!run.IsCancellationRequested)
                {
                    // Only this step was skipped; continue with the next one.
                }
                finally
                {
                    lock (_gate)
                    {
                        _stepCts = null;
                    }
                }
            }

            Set(DemoState.Completed, StepCount);
        }
        catch (OperationCanceledException)
        {
            Set(DemoState.Idle, 0);
        }
        catch (Exception ex)
        {
            LastError = ex;
            Set(DemoState.Failed, CurrentStep);
        }
        finally
        {
            host.PulseProducts(false);
            lock (_gate)
            {
                _runCts?.Dispose();
                _runCts = null;
            }
        }

        return State;
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State == DemoState.Running)
            {
                _resume = NewGate();
                Set(DemoState.Paused, CurrentStep);
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (State == DemoState.Paused)
            {
                Set(DemoState.Running, CurrentStep);
                _resume.TrySetResult();
            }
        }
    }

    /// <summary>Skips the step in progress only.</summary>
    public void SkipStep()
    {
        lock (_gate)
        {
            if (!IsActive)
            {
                return;
            }

            _stepCts?.Cancel();
            if (State == DemoState.Paused)
            {
                Set(DemoState.Running, CurrentStep);
                _resume.TrySetResult();
            }
        }
    }

    /// <summary>Aborts the whole demo, or closes a finished/failed one.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (IsActive)
            {
                _runCts?.Cancel();
                _resume.TrySetResult();
            }
            else
            {
                Set(DemoState.Idle, 0);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _runCts?.Cancel();
        }
    }

    private async Task RunStepAsync(int step, IGuidedDemoHost host, CancellationToken ct)
    {
        var text = host.Text;
        switch (step)
        {
            case 1:
                await SendOrFailAsync(host, text.AskGaming, ct);
                break;

            case 2:
                await SendOrFailAsync(host, text.AnswerUsage, ct);
                if (host.ProductCount > 0)
                {
                    host.PulseProducts(true);
                    SetAwaitingUserTurn(true);
                    await DelayIfPositiveAsync(ProductShowcaseDelay, ct);
                    SetAwaitingUserTurn(false);
                    host.PulseProducts(false);
                }

                break;

            case 3:
                if (host.ProductCount > 0)
                {
                    await SendOrFailAsync(host, text.AskWhere, ct);
                }

                break;

            case 4:
                if (host.ProductCount > 0)
                {
                    await SendOrFailAsync(host, text.AskGuide, ct);
                }

                break;
        }
    }

    private async Task SendOrFailAsync(IGuidedDemoHost host, string message, CancellationToken ct)
    {
        await WaitIfPausedAsync(ct);

        SetAwaitingUserTurn(true);
        await DelayIfPositiveAsync(BeforeUserMessageDelay, ct);
        await WaitIfPausedAsync(ct);
        SetAwaitingUserTurn(false);

        if (!await host.SendAsync(message, ct))
        {
            throw new InvalidOperationException("The assistant did not answer; stopping the demo.");
        }

        await DelayIfPositiveAsync(AfterAssistantDelay, ct);
    }

    private async Task DelayIfPositiveAsync(TimeSpan delay, CancellationToken ct)
    {
        await WaitIfPausedAsync(ct);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var end = DateTime.UtcNow + delay;
        while (DateTime.UtcNow < end)
        {
            await WaitIfPausedAsync(ct);
            var remaining = end - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var slice = TimeSpan.FromMilliseconds(200);
            await Task.Delay(remaining < slice ? remaining : slice, ct);
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken ct)
    {
        while (true)
        {
            Task gate;
            lock (_gate)
            {
                if (State != DemoState.Paused)
                {
                    return;
                }

                gate = _resume.Task;
            }

            await gate.WaitAsync(ct);
        }
    }

    private void SetAwaitingUserTurn(bool awaiting)
    {
        if (AwaitingUserTurn == awaiting)
        {
            return;
        }

        AwaitingUserTurn = awaiting;
        Changed?.Invoke();
    }

    private void Set(DemoState state, int step)
    {
        State = state;
        CurrentStep = step;
        Changed?.Invoke();
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
