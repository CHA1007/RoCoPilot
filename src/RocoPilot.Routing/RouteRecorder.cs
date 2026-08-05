using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Input;

namespace RocoPilot.Routing;

public sealed class RouteRecorder
{
    private readonly IInputDriver _driver;
    private readonly ICaptureSource _capture;
    private readonly RouteStore _store;
    private readonly MinimapSnapshotRegion _minimapRegion;
    private readonly TimeSpan _keyframeInterval;
    private readonly object _gate = new();
    private RecordingSession? _session;
    private CancellationTokenSource? _keyframeLoop;
    private Task? _keyframeTask;

    public RouteRecorder(
        IInputDriver driver,
        ICaptureSource capture,
        RouteStore store,
        MinimapSnapshotRegion? minimapRegion = null,
        TimeSpan? keyframeInterval = null)
    {
        _driver = driver;
        _capture = capture;
        _store = store;
        _minimapRegion = minimapRegion ?? MinimapSnapshotRegion.TopRight;
        _keyframeInterval = keyframeInterval ?? TimeSpan.FromSeconds(2);
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate) return _session is not null;
        }
    }

    public void Start(string name, TimeSpan discoveryTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("录制已在进行——先结束当前录制。");
            }

            var session = new RecordingSession(name);
            _driver.StartStrokeRelay(discoveryTimeout, session.RecordStroke);
            _session = session;

            var cancellation = new CancellationTokenSource();
            _keyframeLoop = cancellation;
            _keyframeTask = Task.Run(() => CaptureKeyframesAsync(session, cancellation.Token));
        }
    }

    public async Task<Route> StopAsync(CancellationToken cancellationToken = default)
    {
        RecordingSession session;
        CancellationTokenSource? keyframeLoop;
        Task? keyframeTask;

        lock (_gate)
        {
            session = _session ?? throw new InvalidOperationException("没有正在进行的录制。");
            keyframeLoop = _keyframeLoop;
            keyframeTask = _keyframeTask;
            _session = null;
            _keyframeLoop = null;
            _keyframeTask = null;
        }

        _driver.StopStrokeRelay();

        keyframeLoop?.Cancel();
        if (keyframeTask is not null)
        {
            try
            {
                await keyframeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        var route = session.Build();
        await _store.SaveAsync(route, cancellationToken).ConfigureAwait(false);
        return route;
    }

    private async Task CaptureKeyframesAsync(RecordingSession session, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_keyframeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                GrabKeyframe(session);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void GrabKeyframe(RecordingSession session)
    {
        if (!_capture.TryGrabLatest(out var frame) || frame is null) return;

        using (frame)
        {
            var (x, y, width, height) = _minimapRegion.Resolve(frame.Width, frame.Height);
            if (width <= 0 || height <= 0) return;

            var png = MinimapPngEncoder.CropToPng(frame, x, y, width, height);
            session.AddKeyframe(new RouteKeyframe(session.ElapsedMs, width, height, png));
        }
    }

    private sealed class RecordingSession(string name)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private readonly List<RouteEvent> _events = [];
        private readonly List<RouteKeyframe> _keyframes = [];

        public string Name { get; } = name;

        public DateTimeOffset RecordedAt { get; } = DateTimeOffset.Now;

        public double ElapsedMs => _clock.Elapsed.TotalMilliseconds;

        public void RecordStroke(ReceivedStroke stroke)
        {
            var offsetMs = ElapsedMs;
            lock (_gate) _events.Add(new RouteEvent(offsetMs, stroke));
        }

        public void AddKeyframe(RouteKeyframe keyframe)
        {
            lock (_gate) _keyframes.Add(keyframe);
        }

        public Route Build()
        {
            lock (_gate)
            {
                _clock.Stop();
                return new Route(Name, RecordedAt, _clock.Elapsed, [.. _events], [.. _keyframes]);
            }
        }
    }
}
