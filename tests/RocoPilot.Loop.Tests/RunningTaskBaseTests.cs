using RocoPilot.Core;

namespace RocoPilot.Loop.Tests;

public class RunningTaskBaseTests
{
    [Fact]
    public async Task StartFlipsIdleToArmingAndInvokesWorker()
    {
        var entered = new TaskCompletionSource();
        var task = new StubTask { Worker = _ => { entered.TrySetResult(); return NeverCompleting; } };
        task.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TaskState.Arming, task.State);
    }

    [Fact]
    public void StartWhenNotIdleThrows()
    {
        var task = new StubTask();
        task.Start();
        Assert.Throws<InvalidOperationException>(() => task.Start());
    }

    [Fact]
    public void RequestStopFromArmingFlipsToStopping()
    {
        var task = new StubTask();
        task.Start();
        task.RequestStop();
        Assert.Equal(TaskState.Stopping, task.State);
    }

    [Fact]
    public void RequestStopWhenIdleIsNoOp()
    {
        var task = new StubTask();
        task.RequestStop();
        Assert.Equal(TaskState.Idle, task.State);
    }

    [Fact]
    public void TryEnterRunningFromArmingSucceeds()
    {
        var task = new StubTask();
        task.Start();
        Assert.True(task.EnterRunning());
        Assert.Equal(TaskState.Running, task.State);
    }

    [Fact]
    public void TryEnterRunningFromIdleFails()
    {
        var task = new StubTask();
        Assert.False(task.EnterRunning());
        Assert.Equal(TaskState.Idle, task.State);
    }

    [Fact]
    public void FinishStoppedRaisesIdleAndCompletesWhenStopped()
    {
        var states = new List<TaskState>();
        var task = new StubTask();
        task.StateChanged += (_, s) => states.Add(s);
        task.Start();
        task.RequestStop();
        task.Finish();
        Assert.Equal(TaskState.Idle, task.State);
        Assert.True(task.WhenStopped.IsCompleted);
        Assert.Equal(TaskState.Idle, states[^1]);
    }

    [Fact]
    public void WhenStoppedIsCompletedBeforeStart()
    {
        var task = new StubTask();
        Assert.True(task.WhenStopped.IsCompleted);
    }

    [Fact]
    public void RaiseEventInvokesSubscribers()
    {
        ToolEvent? received = null;
        var task = new StubTask();
        task.EventRaised += (_, e) => received = e;
        var ev = new ToolEvent("ping");
        task.Raise(ev);
        Assert.Same(ev, received);
    }

    [Fact]
    public void SafeRaiseEventSwallowsHandlerException()
    {
        var task = new StubTask();
        task.EventRaised += (_, _) => throw new InvalidOperationException();
        task.SafeRaise(new ToolEvent("boom"));
    }

    [Fact]
    public void DisposeCancelsWorkerAndReturnsToIdle()
    {
        var task = new StubTask();
        task.Worker = async ct =>
        {
            try { await Task.Delay(-1, ct); }
            catch (TaskCanceledException) { }
            finally { task.Finish(); }
        };
        task.Start();
        task.Dispose();
        Assert.Equal(TaskState.Idle, task.State);
    }

    private static Task NeverCompleting => new TaskCompletionSource().Task;

    private sealed class StubTask : RunningTaskBase
    {
        public override string ToolId => "stub";

        public Func<CancellationToken, Task> Worker { get; set; } = _ => new TaskCompletionSource().Task;

        public override void RequestPause(string source = "manual") { }

        public override void RequestResume(string source = "manual") { }

        protected override Task RunWorkerAsync(CancellationToken token) => Worker(token);

        public bool EnterRunning() => TryEnterRunning();

        public void Finish() => FinishStopped();

        public void Raise(ToolEvent e) => RaiseEvent(e);

        public void SafeRaise(ToolEvent e) => SafeRaiseEvent(e);
    }
}