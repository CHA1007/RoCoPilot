using RocoPilot.Core;
using RocoPilot.Handpan;
using RocoPilot.Input;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanProbeTests
{
    private static readonly ushort KeyB = InputKey.Parse("B").VirtualKey;
    private static readonly ushort KeyT = InputKey.Parse("T").VirtualKey;
    private static readonly ushort KeyU = InputKey.Parse("U").VirtualKey;
    private static readonly ushort MouseLeft = InputKey.LeftMouse.VirtualKey;
    private static readonly (ushort Key, bool Down)[] WakeClick =
        [(MouseLeft, true), (MouseLeft, false)];

    private static HandpanSettings ProbeSettings(params KeyMapEntry[] entries) =>
        new()
        {
            Mode = HandpanMode.Probe,
            KeyMapEntries = [.. entries],
        };

    private static HandpanRunningTask CreateTask(
        HandpanSettings settings,
        RecordingDriver driver,
        Func<bool>? gameFocused = null,
        Func<bool>? activateGameWindow = null,
        Func<(int X, int Y)?>? wakePoint = null) =>
        new(settings, () => driver, gameFocused ?? (() => true), activateGameWindow ?? (() => true), wakePoint: wakePoint ?? (() => (960, 540)));

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("等待超时");
            }

            await Task.Delay(10);
        }
    }

    private sealed class EventLog
    {
        private readonly List<HandpanTaskEvent> _events = [];

        public void Attach(IRunningTask task) =>
            task.EventRaised += (_, toolEvent) =>
            {
                lock (_events)
                {
                    if (toolEvent.Payload is HandpanTaskEvent handpanEvent)
                    {
                        _events.Add(handpanEvent);
                    }
                }
            };

        public IReadOnlyList<HandpanTaskEvent> Events
        {
            get { lock (_events) { return _events.ToList(); } }
        }

        public IReadOnlyList<T> Of<T>()
            where T : HandpanTaskEvent => Events.OfType<T>().ToList();
    }

    [Fact]
    public async Task Probing_presses_each_key_and_logs_the_note_it_should_sound()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(
            ProbeSettings(new KeyMapEntry("A3", "B"), new KeyMapEntry("C5", "T")),
            driver);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(
            WakeClick.Concat(new[] { (KeyB, true), (KeyB, false), (KeyT, true), (KeyT, false) }),
            driver.Strokes);
        Assert.Equal(
            [("B", "A3"), ("T", "C5")],
            log.Of<HandpanProbeKey>().Select(key => (key.Key, key.Note)));
        Assert.Equal(2, Assert.Single(log.Of<HandpanProbeCompleted>()).KeyCount);
        Assert.Equal(1, driver.ArmCount);
        Assert.Equal(1, driver.DisposeCount);
    }

    [Fact]
    public async Task Probing_skips_the_arrangement_and_the_countdown()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = ProbeSettings(new KeyMapEntry("A3", "B"));
        settings.CountdownSeconds = 3;
        using var task = CreateTask(settings, driver);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(log.Of<HandpanArranged>());
        Assert.Empty(log.Of<HandpanCountdown>());
        Assert.Empty(log.Of<HandpanFaulted>());
        Assert.Null(task.DiagnosticsContext);
    }

    [Fact]
    public async Task Pausing_a_probe_freezes_its_clock_until_resumed()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(
            ProbeSettings(new KeyMapEntry("A3", "B"), new KeyMapEntry("C5", "T")),
            driver);
        log.Attach(task);

        task.Start();
        await WaitUntilAsync(() => driver.Strokes.Count >= 4);
        task.RequestPause();
        await WaitUntilAsync(() => task.State == TaskState.Paused);
        await Task.Delay(1200);

        Assert.Equal(4, driver.Strokes.Count);
        task.RequestResume();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(
            WakeClick.Concat(new[] { (KeyB, true), (KeyB, false), (KeyT, true), (KeyT, false) }),
            driver.Strokes);
        Assert.Equal(PauseSource.Manual, Assert.Single(log.Of<HandpanPaused>()).Source);
        Assert.Equal(PauseSource.Manual, Assert.Single(log.Of<HandpanResumed>()).Source);
        Assert.True(log.Of<HandpanProbeCompleted>().Count == 1);
    }

    [Fact]
    public async Task Losing_focus_pauses_a_probe_and_regaining_it_finishes_it()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        var focused = true;
        using var task = CreateTask(
            ProbeSettings(new KeyMapEntry("A3", "B"), new KeyMapEntry("C5", "T"), new KeyMapEntry("E5", "U")),
            driver,
            () => Volatile.Read(ref focused));
        log.Attach(task);

        task.Start();
        await WaitUntilAsync(() => task.State == TaskState.Running);
        Volatile.Write(ref focused, false);
        await WaitUntilAsync(() => task.State == TaskState.Paused);
        Volatile.Write(ref focused, true);
        await WaitUntilAsync(() => task.State == TaskState.Running);
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(PauseSource.FocusLost, Assert.Single(log.Of<HandpanPaused>()).Source);
        Assert.Equal(PauseSource.FocusLost, Assert.Single(log.Of<HandpanResumed>()).Source);
        Assert.Equal(3, Assert.Single(log.Of<HandpanProbeCompleted>()).KeyCount);
        Assert.Equal(
            WakeClick.Concat(new[]
            {
                (KeyB, true), (KeyB, false),
                (KeyT, true), (KeyT, false),
                (KeyU, true), (KeyU, false),
            }),
            driver.Strokes);
    }

    [Fact]
    public async Task An_unusable_key_map_faults_the_probe_without_pressing()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(ProbeSettings(new KeyMapEntry("X9", "Q")), driver);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var fault = Assert.Single(log.Of<HandpanFaulted>());
        Assert.Equal("键位表没有可用条目", fault.Error);
        Assert.NotNull(fault.Remedy);
        Assert.Equal(WakeClick, driver.Strokes);
        Assert.Empty(log.Of<HandpanProbeKey>());
        Assert.Empty(log.Of<HandpanProbeCompleted>());
    }

    [Fact]
    public async Task A_game_window_that_will_not_activate_aborts_probing()
    {
        var driver = new RecordingDriver();
        var names = new List<string>();
        using var task = CreateTask(
            ProbeSettings(new KeyMapEntry("A3", "B")),
            driver,
            activateGameWindow: () => false);
        task.EventRaised += (_, toolEvent) => { lock (names) { names.Add(toolEvent.Name); } };

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(driver.Strokes);
        Assert.Equal(0, driver.ArmCount);
        Assert.Contains(Arming.FailedEvent, names);
    }
}
