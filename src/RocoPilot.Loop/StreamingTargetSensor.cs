using System.Diagnostics.CodeAnalysis;
using RocoPilot.Capture;
using RocoPilot.Detection;

namespace RocoPilot.Loop;

public sealed class StreamingTargetSensor : ICenteringSensor, IDisposable
{
    private readonly ICaptureSource _source;
    private readonly IDetector _detector;
    private readonly StabilityGate _stabilityGate;
    private readonly bool _retainFrames;
    private readonly int _minIntervalMs;
    private readonly object _snapshotLock = new();

    private IReadOnlyList<StableTarget> _snapshot = [];
    private (int Width, int Height) _frameSize;
    private long _lastSequence = -1;
    private long _lastDetectMs;

    private byte[]? _retainedPixels;
    private int _retainedWidth;
    private int _retainedHeight;
    private long _retainedSequence;
    private DateTimeOffset _retainedCapturedAt;
    private IReadOnlyList<DetectedBox> _retainedDetections = [];
    private readonly Dictionary<int, string> _trackClasses = new();

    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private SemaphoreSlim? _frameSignal;
    private int _started;
    private int _suspended;

    public StreamingTargetSensor(ICaptureSource source, IDetector detector, StabilityGate gate, bool retainFrames = false, int minIntervalMs = 0)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _stabilityGate = gate ?? throw new ArgumentNullException(nameof(gate));
        _retainFrames = retainFrames;
        _minIntervalMs = Math.Max(0, minIntervalMs);
    }

    public event EventHandler<Exception>? Faulted;

    public event EventHandler<RecognitionFlip>? RecognitionFlipped;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _frameSignal = new SemaphoreSlim(0);
        _source.FrameArrived += OnFrameArrived;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "居中探测" };
        _worker.Start();
    }

    public void Suspend()
    {
        Interlocked.Exchange(ref _suspended, 1);
        lock (_snapshotLock)
        {
            _snapshot = [];
        }
    }

    public void Resume() => Interlocked.Exchange(ref _suspended, 0);

    public IReadOnlyList<StableTarget> ObserveStable()
    {
        lock (_snapshotLock)
        {
            return _snapshot;
        }
    }

    public (int Width, int Height) LatestFrameSize
    {
        get { lock (_snapshotLock) { return _frameSize; } }
    }

    public bool TrySnapshot([NotNullWhen(true)] out FrameSnapshot? snapshot)
    {
        lock (_snapshotLock)
        {
            if (_retainedPixels is null)
            {
                snapshot = null;
                return false;
            }

            var pixels = new byte[_retainedPixels.Length];
            Buffer.BlockCopy(_retainedPixels, 0, pixels, 0, pixels.Length);
            snapshot = new FrameSnapshot(
                pixels, _retainedWidth, _retainedHeight, _retainedSequence, _retainedCapturedAt,
                _retainedDetections.ToList());
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _source.FrameArrived -= OnFrameArrived;
        _cts?.Cancel();
        var worker = _worker;
        if (worker is not null && worker.IsAlive)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        _cts?.Dispose();
        _frameSignal?.Dispose();
        _cts = null;
        _frameSignal = null;
        _worker = null;
    }

    private void OnFrameArrived(object? sender, EventArgs e)
    {
        try
        {
            _frameSignal?.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void WorkerLoop()
    {
        var cts = _cts!;
        var signal = _frameSignal!;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    signal.Wait(250, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (signal.Wait(0)) { }

                if (_minIntervalMs > 0)
                {
                    var elapsed = Environment.TickCount64 - _lastDetectMs;
                    if (elapsed < _minIntervalMs)
                    {
                        try { signal.Wait((int)(_minIntervalMs - elapsed), cts.Token); } catch (OperationCanceledException) { break; }
                    }
                }

                if (Volatile.Read(ref _suspended) == 1)
                {
                    continue;
                }

                if (!_source.TryGrabLatest(out var frame) || frame is null)
                {
                    continue;
                }

                using (frame)
                {
                    if (frame.Pixels.IsEmpty)
                    {
                        continue;
                    }

                    if (frame.Sequence == _lastSequence)
                    {
                        continue;
                    }

                    _lastSequence = frame.Sequence;

                    try
                    {
                        _lastDetectMs = Environment.TickCount64;
                        var boxes = _detector.Detect(frame.Pixels, frame.Width, frame.Height);
                        var stable = _stabilityGate.Update(boxes);
                        List<RecognitionFlip>? flips = null;
                        lock (_snapshotLock)
                        {
                            _snapshot = stable;
                            _frameSize = (frame.Width, frame.Height);
                            if (_retainFrames)
                            {
                                RetainFrame(frame, boxes);
                                flips = DetectFlips(stable);
                            }
                        }

                        RaiseFlips(flips);
                    }
                    catch (Exception ex)
                    {
                        Faulted?.Invoke(this, ex);
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RetainFrame(CapturedFrame frame, IReadOnlyList<DetectedBox> boxes)
    {
        var length = frame.Pixels.Length;
        if (_retainedPixels is null || _retainedPixels.Length != length)
        {
            _retainedPixels = new byte[length];
        }

        frame.Pixels.CopyTo(_retainedPixels);
        _retainedWidth = frame.Width;
        _retainedHeight = frame.Height;
        _retainedSequence = frame.Sequence;
        _retainedCapturedAt = frame.CapturedAt;
        _retainedDetections = boxes;
    }

    private List<RecognitionFlip>? DetectFlips(IReadOnlyList<StableTarget> stable)
    {
        List<RecognitionFlip>? flips = null;
        foreach (var target in stable)
        {
            var cls = target.Latest.ClassName;
            if (_trackClasses.TryGetValue(target.TrackId, out var previous) &&
                !string.Equals(previous, cls, StringComparison.Ordinal))
            {
                (flips ??= []).Add(new RecognitionFlip(target.TrackId, previous, target));
            }

            _trackClasses[target.TrackId] = cls;
        }

        if (_trackClasses.Count > stable.Count)
        {
            List<int>? dead = null;
            foreach (var trackId in _trackClasses.Keys)
            {
                var alive = false;
                foreach (var target in stable)
                {
                    if (target.TrackId == trackId)
                    {
                        alive = true;
                        break;
                    }
                }

                if (!alive)
                {
                    (dead ??= []).Add(trackId);
                }
            }

            if (dead is not null)
            {
                foreach (var trackId in dead)
                {
                    _trackClasses.Remove(trackId);
                }
            }
        }

        return flips;
    }

    private void RaiseFlips(List<RecognitionFlip>? flips)
    {
        if (flips is not { Count: > 0 })
        {
            return;
        }

        var handlers = RecognitionFlipped;
        if (handlers is null)
        {
            return;
        }

        foreach (var flip in flips)
        {
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<RecognitionFlip>)handler)(this, flip);
                }
                catch
                {
                }
            }
        }
    }
}
