using RocoPilot.Core;
using RocoPilot.Handpan;
using RocoPilot.Input;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanRunningTaskTests
{
    private static readonly ushort KeyT = InputKey.Parse("T").VirtualKey;
    private static readonly ushort KeyY = InputKey.Parse("Y").VirtualKey;
    private static readonly ushort KeyU = InputKey.Parse("U").VirtualKey;
    private static readonly ushort MouseLeft = InputKey.LeftMouse.VirtualKey;
    private static readonly (ushort Key, bool Down)[] WakeClick =
        [(MouseLeft, true), (MouseLeft, false)];

    private static HandpanSettings Settings(TempScore score, int countdownSeconds = 0) =>
        new()
        {
            ScoreName = score.Name,
            CountdownSeconds = countdownSeconds,
        };

    private static HandpanRunningTask CreateTask(
        HandpanSettings settings,
        RecordingDriver driver,
        HandpanScoreStore store,
        Func<bool>? gameFocused = null,
        Func<bool>? activateGameWindow = null,
        Func<(int X, int Y)?>? wakePoint = null) =>
        new(settings, () => driver, gameFocused ?? (() => true), activateGameWindow ?? (() => true), store, wakePoint ?? (() => (960, 540)));

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

    private sealed class ThrowingDriver : RecordingDriver
    {
        public override void KeyDown(InputKey key)
        {
            if (key.VirtualKey == MouseLeft)
            {
                base.KeyDown(key);
                return;
            }

            throw new InvalidOperationException("注入失败");
        }
    }

    private sealed class StickyDriver : RecordingDriver
    {
        public override void KeyUp(InputKey key)
        {
            if (key.VirtualKey == MouseLeft)
            {
                base.KeyUp(key);
                return;
            }

            throw new InvalidOperationException("抬键失败");
        }
    }

    private sealed class EventLog
    {
        private readonly List<HandpanTaskEvent> _events = [];
        private readonly List<string> _names = [];

        public void Attach(IRunningTask task) =>
            task.EventRaised += (_, toolEvent) =>
            {
                lock (_events)
                {
                    _names.Add(toolEvent.Name);
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

        public IReadOnlyList<string> Names
        {
            get { lock (_events) { return _names.ToList(); } }
        }

        public T? First<T>()
            where T : HandpanTaskEvent => Events.OfType<T>().FirstOrDefault();

        public bool Contains<T>()
            where T : HandpanTaskEvent => Events.OfType<T>().Any();
    }

    private sealed class ProgressLog
    {
        private readonly List<HandpanProgress> _samples = [];

        public void Attach(HandpanRunningTask task) =>
            task.ProgressChanged += sample =>
            {
                lock (_samples)
                {
                    _samples.Add(sample);
                }
            };

        public IReadOnlyList<HandpanProgress> Samples
        {
            get { lock (_samples) { return _samples.ToList(); } }
        }
    }

    [Fact]
    public async Task Playing_sends_the_planned_key_events_in_order()
    {
        using var score = TempScore.WithNotes((0, 72), (1, 74));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(Settings(score, countdownSeconds: 1), driver, library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(
            WakeClick.Concat(new[] { (KeyT, true), (KeyT, false), (KeyY, true), (KeyY, false) }),
            driver.Strokes);
        Assert.Equal(1, Assert.Single(log.Events.OfType<HandpanCountdown>()).SecondsLeft);
        Assert.True(log.Contains<HandpanCompleted>());
        Assert.Equal(1, driver.ArmCount);
        Assert.Equal(1, driver.DisposeCount);
    }

    [Fact]
    public async Task The_arrangement_is_published_before_playing()
    {
        using var score = TempScore.WithNotes((0, 72), (1, 74));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(Settings(score), driver, library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var arranged = log.First<HandpanArranged>()!;
        Assert.Equal(2, arranged.NoteCount);
        Assert.Equal(0, arranged.MissingCount);
        Assert.Equal(0, arranged.Semitones);
        Assert.Equal(1, arranged.ExactCoverage);
        Assert.Equal(0.75, arranged.TotalSeconds, 6);
    }

    [Fact]
    public async Task A_missing_midi_file_arms_with_a_remedy_and_plays_nothing()
    {
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var library = TempScoreLibrary.Create();
        using var task = CreateTask(
            new HandpanSettings { ScoreName = "不存在的曲谱" },
            driver,
            library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(driver.Strokes);
        Assert.Contains(Arming.FailedEvent, log.Names);
        Assert.Equal(TaskState.Idle, task.State);
    }

    [Fact]
    public async Task Stopping_releases_the_held_key()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var settings = Settings(score);
        settings.HoldMs = 500;
        using var task = CreateTask(settings, driver, library.Store);

        task.Start();
        await WaitUntilAsync(() => driver.Strokes.Count > 2);
        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(WakeClick.Concat(new[] { (KeyT, true), (KeyT, false) }), driver.Strokes);
    }

    [Fact]
    public async Task An_emergency_stop_breaks_through_the_pause_gate_and_leaves_no_key_held()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var settings = Settings(score);
        settings.HoldMs = 500;
        using var task = CreateTask(settings, driver, library.Store);

        task.Start();
        await WaitUntilAsync(() => driver.Strokes.Count(s => s.Key != MouseLeft) > 0);
        task.RequestPause();
        await WaitUntilAsync(() => task.State == TaskState.Paused);

        ((IRunningTask)task).RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(TaskState.Idle, task.State);
        Assert.Equal(WakeClick.Concat(new[] { (KeyT, true), (KeyT, false) }), driver.Strokes);
        Assert.Equal(1, driver.DisposeCount);
    }

    [Fact]
    public async Task Pausing_releases_the_held_key_and_reports_typed_events()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        settings.HoldMs = 500;
        using var task = CreateTask(settings, driver, library.Store);
        log.Attach(task);

        task.Start();
        await WaitUntilAsync(() => driver.Strokes.Count(s => s.Key != MouseLeft) > 0);
        task.RequestPause();
        await WaitUntilAsync(() => task.State == TaskState.Paused);
        await WaitUntilAsync(() => driver.Strokes.Count(stroke => !stroke.Down) >= 2);

        task.RequestResume();
        await WaitUntilAsync(() => task.State == TaskState.Running);
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(PauseSource.Manual, log.First<HandpanPaused>()!.Source);
        Assert.Equal(PauseSource.Manual, log.First<HandpanResumed>()!.Source);
        Assert.True(log.Contains<HandpanCompleted>());
        Assert.Equal(
            WakeClick.Concat(new[]
            {
                (KeyT, true), (KeyT, false),
                (KeyY, true), (KeyY, false),
                (KeyU, true), (KeyU, false),
            }),
            driver.Strokes);
    }

    [Fact]
    public async Task Losing_focus_pauses_and_regaining_it_resumes()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76), (6, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var focused = true;
        using var task = CreateTask(Settings(score), driver, library.Store, () => Volatile.Read(ref focused));
        log.Attach(task);

        task.Start();
        await WaitUntilAsync(() => task.State == TaskState.Running);
        Volatile.Write(ref focused, false);
        await WaitUntilAsync(() => task.State == TaskState.Paused);
        Volatile.Write(ref focused, true);
        await WaitUntilAsync(() => task.State == TaskState.Running);

        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(PauseSource.FocusLost, log.First<HandpanPaused>()!.Source);
        Assert.Equal(PauseSource.FocusLost, log.First<HandpanResumed>()!.Source);
    }

    [Fact]
    public async Task A_manual_pause_survives_regaining_focus()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76), (6, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var focused = true;
        using var task = CreateTask(Settings(score), driver, library.Store, () => Volatile.Read(ref focused));

        task.Start();
        await WaitUntilAsync(() => task.State == TaskState.Running);
        task.RequestPause();
        await WaitUntilAsync(() => task.State == TaskState.Paused);

        Volatile.Write(ref focused, false);
        await Task.Delay(400);
        Volatile.Write(ref focused, true);
        await Task.Delay(600);

        Assert.Equal(TaskState.Paused, task.State);
        task.RequestResume();
        await WaitUntilAsync(() => task.State == TaskState.Running);
        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task A_game_window_that_will_not_activate_aborts_arming()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        using var task = CreateTask(Settings(score), driver, library.Store, () => false, () => false);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(driver.Strokes);
        Assert.Contains(Arming.FailedEvent, log.Names);
        Assert.Equal(0, driver.ArmCount);
    }

    [Fact]
    public async Task Arming_activates_the_game_window_once()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var activations = 0;
        using var task = new HandpanRunningTask(
            Settings(score),
            () => driver,
            () => true,
            () => Interlocked.Increment(ref activations) > 0,
            library.Store,
            () => (960, 540));

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(1, Volatile.Read(ref activations));
        Assert.Equal(WakeClick.Concat(new[] { (KeyT, true), (KeyT, false) }), driver.Strokes);
    }

    [Fact]
    public async Task Click_to_wake_clicks_the_game_window_before_the_first_note()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        using var task = new HandpanRunningTask(
            settings,
            () => driver,
            () => true,
            () => true,
            library.Store,
            () => (960, 540));

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(
            WakeClick.Concat(new[] { (KeyT, true), (KeyT, false) }),
            driver.Strokes);
    }

    [Fact]
    public async Task A_wake_click_that_does_not_bring_the_game_forward_aborts_arming()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        using var task = new HandpanRunningTask(
            settings,
            () => driver,
            () => false,
            () => true,
            library.Store,
            () => (960, 540));
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(new[] { (MouseLeft, true), (MouseLeft, false) }, driver.Strokes);
        Assert.Contains(Arming.FailedEvent, log.Names);
    }

    [Fact]
    public async Task A_wake_click_without_a_game_window_aborts_arming()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        using var task = new HandpanRunningTask(
            settings,
            () => driver,
            () => true,
            () => true,
            library.Store,
            () => null);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(driver.Strokes);
        Assert.Contains(Arming.FailedEvent, log.Names);
    }

    [Fact]
    public async Task A_score_without_playable_notes_faults()
    {
        using var score = TempScore.WithNotes((0, 5));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        settings.KeyMapEntries = [new KeyMapEntry("C5", "T")];
        using var task = CreateTask(settings, driver, library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var fault = log.First<HandpanFaulted>()!;
        Assert.Equal("没有可演奏的音符", fault.Error);
        Assert.Equal(WakeClick, driver.Strokes);
    }

    [Fact]
    public async Task The_arrangement_is_the_diagnostics_context()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        using var task = CreateTask(Settings(score), driver, library.Store);

        Assert.Null(task.DiagnosticsContext);
        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var arrangement = Assert.IsType<HandpanArrangement>(task.DiagnosticsContext);
        Assert.Equal(1, arrangement.Plan.NoteCount);
    }

    [Fact]
    public async Task Looping_reports_each_round_until_stopped()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var log = new EventLog();
        var settings = Settings(score);
        settings.Loop = true;
        using var task = CreateTask(settings, driver, library.Store);
        log.Attach(task);

        task.Start();
        await WaitUntilAsync(() => log.Contains<HandpanRoundCompleted>());
        await WaitUntilAsync(() => driver.Strokes.Count(s => s.Key != MouseLeft) >= 4, 20_000);
        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(1, log.First<HandpanRoundCompleted>()!.Round);
        Assert.False(log.Contains<HandpanCompleted>());
        Assert.Equal(
            WakeClick.Concat(new[] { (KeyT, true), (KeyT, false), (KeyT, true), (KeyT, false) }),
            driver.Strokes.Take(6));
    }

    [Fact]
    public async Task Playing_reports_progress_that_climbs_to_the_end_of_the_plan()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var progress = new ProgressLog();
        using var task = CreateTask(Settings(score), driver, library.Store);
        progress.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var samples = progress.Samples;
        Assert.True(samples.Count >= 2, $"只采到 {samples.Count} 个进度");
        Assert.True(samples[0].TotalSeconds > 0);
        Assert.All(samples, sample => Assert.Equal(samples[0].TotalSeconds, sample.TotalSeconds));
        Assert.All(samples, sample => Assert.False(sample.IsLooping));
        for (var index = 1; index < samples.Count; index++)
        {
            Assert.True(samples[index].ElapsedSeconds >= samples[index - 1].ElapsedSeconds);
        }

        Assert.Equal(samples[^1].TotalSeconds, samples[^1].ElapsedSeconds, 6);
        Assert.Equal(1, samples[^1].Ratio, 6);
    }

    [Fact]
    public async Task Looping_progress_counts_the_rounds()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74), (4, 76));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        var progress = new ProgressLog();
        var settings = Settings(score);
        settings.Loop = true;
        using var task = CreateTask(settings, driver, library.Store);
        progress.Attach(task);

        task.Start();
        await WaitUntilAsync(() => progress.Samples.Any(sample => sample.Round == 1 && sample.Ratio < 1), 20_000);
        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        var samples = progress.Samples;
        Assert.Equal(1, samples.Last(sample => sample.Round == 0).Ratio, 6);
        Assert.Equal(2, samples.First(sample => sample.Round == 1).RoundNumber);
        Assert.All(samples, sample => Assert.Equal(samples[0].TotalSeconds, sample.TotalSeconds));
    }

    [Fact]
    public async Task A_driver_that_refuses_to_arm_aborts_arming()
    {
        using var score = TempScore.WithNotes((0, 72));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var log = new EventLog();
        using var task = new HandpanRunningTask(
            Settings(score),
            () => throw new InputDriverException("驱动未运行"),
            () => true,
            () => true,
            library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Contains(Arming.FailedEvent, log.Names);
    }

    [Fact]
    public async Task An_injection_failure_faults_and_stops_playing()
    {
        using var score = TempScore.WithNotes((0, 72), (1, 74));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new ThrowingDriver();
        var log = new EventLog();
        using var task = CreateTask(Settings(score), driver, library.Store);
        log.Attach(task);

        task.Start();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal("注入失败", log.First<HandpanFaulted>()!.Error);
        Assert.False(log.Contains<HandpanCompleted>());
    }

    [Fact]
    public async Task A_driver_that_refuses_to_release_still_stops_the_task()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new StickyDriver();
        var settings = Settings(score);
        settings.HoldMs = 500;
        using var task = CreateTask(settings, driver, library.Store);

        task.Start();
        await WaitUntilAsync(() => driver.Strokes.Count(s => s.Key != MouseLeft) > 0);
        task.RequestStop();
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(TaskState.Idle, task.State);
        Assert.Equal(WakeClick.Concat(new[] { (KeyT, true) }), driver.Strokes);
    }

    [Fact]
    public async Task A_running_task_refuses_a_second_start()
    {
        using var score = TempScore.WithNotes((0, 72), (2, 74));
        using var library = TempScoreLibrary.Create();
        library.Add(score);
        var driver = new RecordingDriver();
        using var task = CreateTask(Settings(score), driver, library.Store);

        task.Start();
        Assert.Throws<InvalidOperationException>(task.Start);
        await task.WhenStopped.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
