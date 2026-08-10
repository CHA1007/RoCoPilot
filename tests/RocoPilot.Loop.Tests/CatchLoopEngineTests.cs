using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Loop.Tests;

public class CatchLoopEngineTests
{
    [Fact]
    public void ConstructorInitializesIdleScanningState()
    {
        var (engine, bus, _) = CreateEngine(mode: CatchLoopMode.Live);
        Assert.Equal(CatchLoopState.Idle, engine.State);
        Assert.Equal(CatchPhase.Scanning, engine.Phase);
        Assert.Equal(CatchLoopMode.Live, engine.Mode);
        Assert.Equal(-1, engine.ActiveTrackId);
        Assert.Same(bus, engine.Bus);
    }

    [Fact]
    public async Task RunEmitsSessionStartStopAndReturnsToIdleOnCancel()
    {
        var (engine, _, events) = CreateEngine();
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => engine.Run(cts.Token));
        WaitUntil(() => HasEvent(events, "session_start"));
        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(CatchLoopState.Idle, engine.State);
        Assert.True(HasEvent(events, "session_stop"));
    }

    [Fact]
    public void PauseReturnsFalseWhenNotRunning()
    {
        var (engine, _, _) = CreateEngine();
        Assert.False(engine.Pause());
    }

    [Fact]
    public async Task PauseResumeTransitionStateAndEmit()
    {
        var (engine, _, events) = CreateEngine();
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => engine.Run(cts.Token));
        WaitUntil(() => HasEvent(events, "session_start"));

        Assert.True(engine.Pause());
        Assert.Equal(CatchLoopState.Paused, engine.State);
        Assert.False(engine.Pause());

        Assert.True(engine.Resume());
        Assert.Equal(CatchLoopState.Running, engine.State);
        Assert.False(engine.Resume());

        Assert.True(HasEvent(events, "paused"));
        Assert.True(HasEvent(events, "resumed"));

        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RunWhenAlreadyRunningThrows()
    {
        var (engine, _, events) = CreateEngine();
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => engine.Run(cts.Token));
        WaitUntil(() => HasEvent(events, "session_start"));
        Assert.Throws<LoopException>(() => engine.Run());
        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task StallAlertEmittedWhenNoTargetDuringLongIdle()
    {
        long now = 0;
        var (engine, _, events) = CreateEngine(
            new CatchLoopOptions { StallAlertMs = 1 },
            nowMs: () => Interlocked.Increment(ref now));
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => engine.Run(cts.Token));
        WaitUntil(() => HasEvent(events, "stall_alert"));
        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void DisposeSucceeds()
    {
        var (engine, _, _) = CreateEngine();
        engine.Dispose();
    }

    [Fact]
    public void ApplyOptionsNullThrows()
    {
        var (engine, _, _) = CreateEngine();
        Assert.Throws<ArgumentNullException>(() => engine.ApplyOptions(null!));
    }

    [Fact]
    public void ApplyOptionsInvalidThrows()
    {
        var (engine, _, _) = CreateEngine();
        var invalid = new CatchLoopOptions { PostSettleDelayMinMs = 0 };
        Assert.Throws<LoopException>(() => engine.ApplyOptions(invalid));
    }

    [Fact]
    public async Task ApplyOptionsMidRunChangesStallThreshold()
    {
        long now = 0;
        var (engine, _, events) = CreateEngine(nowMs: () => Interlocked.Increment(ref now));
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => engine.Run(cts.Token));
        WaitUntil(() => HasEvent(events, "session_start"));
        Assert.False(HasEvent(events, "stall_alert"));

        engine.ApplyOptions(new CatchLoopOptions { StallAlertMs = 1 });

        WaitUntil(() => HasEvent(events, "stall_alert"));
        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static void FastSleep(int milliseconds, CancellationToken cancellationToken)
        => cancellationToken.ThrowIfCancellationRequested();

    private static (CatchLoopEngine Engine, CatchEventBus Bus, List<string> Events) CreateEngine(
        CatchLoopOptions? options = null,
        CatchLoopMode mode = CatchLoopMode.DryRun,
        Func<long>? nowMs = null,
        Func<bool>? inputGate = null)
    {
        var sensor = new NoTargetSensor();
        var driver = new NoopDriver();
        var bus = new CatchEventBus(new CatchCounters());
        var events = new List<string>();
        bus.EventRaised += (_, e) => { lock (events) events.Add(e.Name); };
        var controller = new CenteringController(
            new CenteringOptions(), sensor, driver,
            cache: new CalibrationCache(), sleep: FastSleep, inputGate: () => true);
        var engine = new CatchLoopEngine(
            options ?? new CatchLoopOptions(), mode, sensor, driver, controller, bus,
            random: new Random(1), sleep: FastSleep, nowMs: nowMs ?? (() => 0L), inputGate: inputGate);
        return (engine, bus, events);
    }

    private static bool HasEvent(List<string> events, string name)
    {
        lock (events) return events.Contains(name);
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.True(condition(), "条件未在超时内满足");
    }

    private sealed class NoTargetSensor : ICenteringSensor
    {
        public IReadOnlyList<StableTarget> ObserveStable() => Array.Empty<StableTarget>();

        public (int Width, int Height) LatestFrameSize => (1920, 1080);

        public void SuspendSensing() { }

        public void ResumeSensing() { }

        public void ResetStability() { }
    }

    private sealed class NoopDriver : IInputDriver
    {
        public string BackendName => "noop";

        public void Arm() { }

        public void MoveRelative(int dx, int dy) { }

        public void KeyDown(InputKey key) { }

        public void KeyUp(InputKey key) { }

        public void SendRawStroke(ReceivedStroke stroke) { }

        public void StartStrokeRelay(Action<ReceivedStroke> onStroke) { }

        public void StopStrokeRelay() { }

        public void Dispose() { }
    }
}