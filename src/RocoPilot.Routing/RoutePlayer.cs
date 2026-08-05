using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Input;

namespace RocoPilot.Routing;

public enum PlaybackState
{
    Idle,
    Playing,
    Paused,
}

public enum PlaybackOutcome
{
    Completed,
    Stopped,
    Stuck,
}

public sealed record PlaybackProgress(
    PlaybackState State,
    int SentEvents,
    int EventCount,
    double OffsetMs,
    double DurationMs);

public sealed record PlaybackResult(
    PlaybackOutcome Outcome,
    int SentEvents,
    int EventCount,
    double ReachedOffsetMs);

public sealed class RoutePlayerOptions
{
    public TimeSpan StrokeJitterMax { get; init; } = TimeSpan.FromMilliseconds(15);

    public TimeSpan CumulativeJitterCap { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan StuckThreshold { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StillnessSampleInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public double StillFrameMeanDiff { get; init; } = 2.0;
}

public sealed class RoutePlayer
{
    private const ushort KeyUpState = 0x001;
    private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(10);

    private readonly IInputDriver _driver;
    private readonly FrameStillnessProbe _stillness;
    private readonly RoutePlayerOptions _options;
    private readonly object _gate = new();
    private readonly Random _jitter = new();
    private PlaybackProgress _progress = new(PlaybackState.Idle, 0, 0, 0, 0);
    private bool _playing;
    private volatile bool _pauseRequested;

    public RoutePlayer(IInputDriver driver, ICaptureSource capture, RoutePlayerOptions? options = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _options = options ?? new RoutePlayerOptions();
        _stillness = new FrameStillnessProbe(capture ?? throw new ArgumentNullException(nameof(capture)), _options.StillFrameMeanDiff);
    }

    public PlaybackProgress Progress
    {
        get
        {
            lock (_gate) return _progress;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_playing) _pauseRequested = true;
        }
    }

    public void Resume()
    {
        lock (_gate) _pauseRequested = false;
    }

    public Task<PlaybackResult> PlayAsync(Route route, double startAtOffsetMs = 0, CancellationToken stoppingToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (startAtOffsetMs < 0) throw new ArgumentOutOfRangeException(nameof(startAtOffsetMs));

        lock (_gate)
        {
            if (_playing) throw new InvalidOperationException("回放已在进行——先停止当前回放。");
            _playing = true;
            _pauseRequested = false;
        }

        return Task.Run(() =>
        {
            try
            {
                return PlayCore(route, startAtOffsetMs, stoppingToken);
            }
            finally
            {
                lock (_gate) _playing = false;
            }
        });
    }

    private PlaybackResult PlayCore(Route route, double startAtOffsetMs, CancellationToken stoppingToken)
    {
        var events = route.Events;
        var durationMs = route.Duration.TotalMilliseconds;
        var startIndex = FirstIndexAtOrAfter(events, startAtOffsetMs);

        var clock = Stopwatch.StartNew();
        var heldKeys = new HashSet<ushort>();
        var heldMouseButtons = new HashSet<ushort>();
        var outcome = PlaybackOutcome.Completed;

        double lastRealMs = 0;
        double virtualMs = 0;
        double lastSentOffsetMs = startAtOffsetMs;
        double jitterAccumMs = 0;
        double lastStillnessMs = double.NegativeInfinity;
        double? stuckSinceMs = null;
        var sent = 0;

        var sampleIntervalMs = _options.StillnessSampleInterval.TotalMilliseconds;
        var stuckThresholdMs = _options.StuckThreshold.TotalMilliseconds;
        var jitterMaxMs = _options.StrokeJitterMax.TotalMilliseconds;
        var jitterCapMs = _options.CumulativeJitterCap.TotalMilliseconds;

        Report(PlaybackState.Playing, sent, events.Count, lastSentOffsetMs, durationMs);

        var index = startIndex;
        while (true)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                outcome = PlaybackOutcome.Stopped;
                break;
            }

            var nowRealMs = clock.Elapsed.TotalMilliseconds;
            var paused = _pauseRequested;
            if (paused)
            {
                stuckSinceMs = null;
            }
            else
            {
                virtualMs += nowRealMs - lastRealMs;
            }

            lastRealMs = nowRealMs;
            Report(paused ? PlaybackState.Paused : PlaybackState.Playing, sent, events.Count, lastSentOffsetMs, durationMs);

            if (!paused && virtualMs - lastStillnessMs >= sampleIntervalMs)
            {
                lastStillnessMs = virtualMs;
                if (IsStuck(heldKeys.Count > 0, ref stuckSinceMs, virtualMs, stuckThresholdMs))
                {
                    outcome = PlaybackOutcome.Stuck;
                    break;
                }
            }

            if (index >= events.Count) break;

            var ev = events[index];
            var targetMs = ev.OffsetMs - startAtOffsetMs + jitterAccumMs;
            if (virtualMs < targetMs)
            {
                Thread.Sleep(WaitSlice);
                continue;
            }

            _driver.SendRawStroke(ev.Stroke);
            TrackHeld(ev.Stroke, heldKeys, heldMouseButtons);
            sent++;
            lastSentOffsetMs = ev.OffsetMs;
            index++;

            jitterAccumMs = Math.Clamp(jitterAccumMs + NextJitterMs(jitterMaxMs), -jitterCapMs, jitterCapMs);
        }

        ReleaseHeld(heldKeys, heldMouseButtons);

        var reachedMs = outcome == PlaybackOutcome.Completed ? durationMs : lastSentOffsetMs;
        Report(PlaybackState.Idle, sent, events.Count, reachedMs, durationMs);
        return new PlaybackResult(outcome, sent, events.Count, reachedMs);
    }

    private bool IsStuck(bool keyHeld, ref double? stuckSinceMs, double virtualMs, double thresholdMs)
    {
        if (!keyHeld)
        {
            stuckSinceMs = null;
            return false;
        }

        switch (_stillness.Sample())
        {
            case ScreenChange.Still:
                stuckSinceMs ??= virtualMs;
                return virtualMs - stuckSinceMs.Value >= thresholdMs;
            default:
                stuckSinceMs = null;
                return false;
        }
    }

    private static void TrackHeld(ReceivedStroke stroke, HashSet<ushort> heldKeys, HashSet<ushort> heldMouseButtons)
    {
        if (stroke.Kind == ReceivedDeviceKind.Keyboard)
        {
            if ((stroke.State & KeyUpState) != 0) heldKeys.Remove(stroke.Code);
            else heldKeys.Add(stroke.Code);
            return;
        }

        switch (stroke.State)
        {
            case 0x001: heldMouseButtons.Add(0x001); break;
            case 0x002: heldMouseButtons.Remove(0x001); break;
            case 0x004: heldMouseButtons.Add(0x002); break;
            case 0x008: heldMouseButtons.Remove(0x002); break;
            case 0x010: heldMouseButtons.Add(0x004); break;
            case 0x020: heldMouseButtons.Remove(0x004); break;
        }
    }

    private void ReleaseHeld(HashSet<ushort> heldKeys, HashSet<ushort> heldMouseButtons)
    {
        foreach (var code in heldKeys)
        {
            _driver.SendRawStroke(ReceivedStroke.Key(code, KeyUpState));
        }

        heldKeys.Clear();

        foreach (var button in heldMouseButtons)
        {
            _driver.SendRawStroke(ReceivedStroke.Mouse(MouseUpState(button), 0, 0, 0, 0));
        }

        heldMouseButtons.Clear();
    }

    private static ushort MouseUpState(ushort button) => button switch
    {
        0x001 => 0x002,
        0x002 => 0x008,
        0x004 => 0x020,
        _ => 0,
    };

    private double NextJitterMs(double maxMs) => _jitter.NextDouble() * 2 * maxMs - maxMs;

    private void Report(PlaybackState state, int sent, int count, double offsetMs, double durationMs)
    {
        lock (_gate) _progress = new PlaybackProgress(state, sent, count, offsetMs, durationMs);
    }

    private static int FirstIndexAtOrAfter(IReadOnlyList<RouteEvent> events, double offsetMs)
    {
        var lo = 0;
        var hi = events.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (events[mid].OffsetMs < offsetMs) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}
