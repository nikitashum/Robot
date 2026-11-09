using ButikkPilot.Web.Services;

namespace ButikkPilot.Tests;

public class GuidedDemoRunnerTests
{
    private sealed class FakeHost : IGuidedDemoHost
    {
        public UiText Text { get; set; } = UiText.English;
        public int ProductCount { get; set; } = 3;
        public bool ResetCalled { get; private set; }
        public List<string> Sent { get; } = [];
        public Func<string, CancellationToken, Task<bool>>? OnSend { get; set; }
        public List<bool> Pulses { get; } = [];

        public Task ResetAsync()
        {
            ResetCalled = true;
            return Task.CompletedTask;
        }

        public async Task<bool> SendAsync(string message, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Sent.Add(message);
            return OnSend is null || await OnSend(message, ct);
        }

        public void PulseProducts(bool on) => Pulses.Add(on);
    }

    private static GuidedDemoRunner Runner() => new() { PaceDelay = TimeSpan.Zero };

    [Fact]
    public async Task Runs_all_four_steps_in_order_and_ends_completed()
    {
        var host = new FakeHost();
        var runner = Runner();

        var state = await runner.RunAsync(host);

        Assert.Equal(DemoState.Completed, state);
        Assert.True(host.ResetCalled);
        Assert.Equal([host.Text.AskGaming, host.Text.AnswerUsage, host.Text.AskWhere, host.Text.AskGuide], host.Sent);
        Assert.False(host.Pulses.Last()); // never leaves the product cards pulsing
    }

    [Fact]
    public async Task Skipping_a_step_aborts_only_that_step()
    {
        var host = new FakeHost();
        var runner = Runner();
        host.OnSend = async (msg, ct) =>
        {
            if (msg == host.Text.AnswerUsage)
            {
                runner.SkipStep();           // user presses Skip during step 2
                await Task.Delay(Timeout.Infinite, ct);
            }

            return true;
        };

        var state = await runner.RunAsync(host);

        Assert.Equal(DemoState.Completed, state);
        Assert.Contains(host.Text.AskWhere, host.Sent);   // steps 3 and 4 still ran
        Assert.Contains(host.Text.AskGuide, host.Sent);
    }

    [Fact]
    public async Task Stop_aborts_the_whole_demo_and_returns_to_idle()
    {
        var host = new FakeHost();
        var runner = Runner();
        host.OnSend = async (msg, ct) =>
        {
            if (msg == host.Text.AskGaming)
            {
                runner.Stop();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return true;
        };

        var state = await runner.RunAsync(host);

        Assert.Equal(DemoState.Idle, state);
        Assert.Single(host.Sent);
    }

    [Fact]
    public async Task A_failed_message_ends_in_Failed_and_the_demo_can_be_replayed()
    {
        var host = new FakeHost { OnSend = (_, _) => Task.FromResult(false) };
        var runner = Runner();

        Assert.Equal(DemoState.Failed, await runner.RunAsync(host));
        Assert.NotNull(runner.LastError);

        host.OnSend = null;
        host.Sent.Clear();
        Assert.Equal(DemoState.Completed, await runner.RunAsync(host));
        Assert.Equal(4, host.Sent.Count);
        Assert.Null(runner.LastError);
    }

    [Fact]
    public async Task An_unexpected_exception_is_contained()
    {
        var host = new FakeHost { OnSend = (_, _) => throw new InvalidOperationException("boom") };
        var runner = Runner();

        var state = await runner.RunAsync(host);

        Assert.Equal(DemoState.Failed, state);
        Assert.IsType<InvalidOperationException>(runner.LastError);
    }

    [Fact]
    public async Task Completed_demo_can_be_replayed()
    {
        var host = new FakeHost();
        var runner = Runner();

        await runner.RunAsync(host);
        host.Sent.Clear();
        var again = await runner.RunAsync(host);

        Assert.Equal(DemoState.Completed, again);
        Assert.Equal(4, host.Sent.Count);
    }

    [Fact]
    public async Task Pause_really_pauses_until_resumed()
    {
        var host = new FakeHost();
        var runner = Runner();
        var inFirstSend = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        host.OnSend = async (_, _) =>
        {
            if (host.Sent.Count == 1)
            {
                inFirstSend.TrySetResult();
                await release.Task;      // hold step 1 open so the test can pause deterministically
            }

            return true;
        };

        var run = runner.RunAsync(host);
        await inFirstSend.Task;
        runner.Pause();
        release.SetResult();
        await Task.Delay(150);

        Assert.Equal(DemoState.Paused, runner.State);
        Assert.Single(host.Sent);                         // nothing new is sent while paused
        Assert.False(run.IsCompleted);

        runner.Resume();
        Assert.Equal(DemoState.Completed, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(4, host.Sent.Count);
    }

    [Fact]
    public async Task Stopping_while_paused_does_not_hang()
    {
        var host = new FakeHost();
        var runner = Runner();
        var inFirstSend = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        host.OnSend = async (_, _) =>
        {
            inFirstSend.TrySetResult();
            await release.Task;
            return true;
        };

        var run = runner.RunAsync(host);
        await inFirstSend.Task;
        runner.Pause();
        release.SetResult();
        await Task.Delay(50);
        runner.Stop();

        Assert.Equal(DemoState.Idle, await run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Starting_twice_does_not_start_a_second_run()
    {
        var host = new FakeHost();
        var runner = new GuidedDemoRunner { PaceDelay = TimeSpan.FromMilliseconds(30) };

        var first = runner.RunAsync(host);
        var second = await runner.RunAsync(new FakeHost());

        Assert.True(second is DemoState.Running or DemoState.Paused);
        await first;
        Assert.Equal(4, host.Sent.Count);
    }

    [Fact]
    public async Task Steps_that_need_products_are_skipped_when_none_were_recommended()
    {
        var host = new FakeHost { ProductCount = 0 };
        var runner = Runner();

        var state = await runner.RunAsync(host);

        Assert.Equal(DemoState.Completed, state);
        Assert.Equal([host.Text.AskGaming, host.Text.AnswerUsage], host.Sent);
    }

    [Fact]
    public async Task State_changes_are_announced()
    {
        var runner = Runner();
        var seen = new List<(DemoState, int)>();
        runner.Changed += () => seen.Add((runner.State, runner.CurrentStep));

        await runner.RunAsync(new FakeHost());

        Assert.Contains((DemoState.Running, 1), seen);
        Assert.Contains((DemoState.Running, 4), seen);
        Assert.Equal(DemoState.Completed, seen.Last().Item1);
    }
}
