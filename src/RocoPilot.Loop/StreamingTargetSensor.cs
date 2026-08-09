using RocoPilot.Capture;
using RocoPilot.Detection;

namespace RocoPilot.Loop;

public sealed class StreamingTargetSensor : ICenteringSensor, IDisposable
{
    private readonly ICaptureSource _source;
    private readonly IDetector _detector;
    private readonly StabilityGate _stabilityGate;
    private readonly int _minIntervalMs;
    private readonly object _snapshotLock = new();

    private IReadOnlyList<StableTarget> _snapshot = [];
    private (int Width, int Height) _frameSize;
    private long _lastSequence = -1;
    private long _lastDetectMs;

    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private SemaphoreSlim? _frameSignal;
    private int _started;
    private int _suspended;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StreamingTargetSensor(ICaptureSource source, IDetector detector, StabilityGate gate, int minIntervalMs = 0)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _stabilityGate = gate ?? throw new ArgumentNullException(nameof(gate));
        _minIntervalMs = Math.Max(0, minIntervalMs);
    }

    public event EventHandler<Exception>? Faulted;

    public Task FirstFrameArrived => _firstFrame.Task;

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

    public void SuspendSensing() => Suspend();

    public void ResumeSensing() => Resume();

    public void ResetStability()
    {
        _stabilityGate.Reset();
        lock (_snapshotLock)
        {
            _snapshot = [];
        }
    }

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
                    lock (_snapshotLock)
                    {
                        _frameSize = (frame.Width, frame.Height);
                    }

                    _firstFrame.TrySetResult();

                    try
                    {
                        _lastDetectMs = Environment.TickCount64;
                        var boxes = _detector.Detect(frame.Pixels, frame.Width, frame.Height);
                        var stable = _stabilityGate.Update(boxes);
                        lock (_snapshotLock)
                        {
                            _snapshot = stable;
                        }
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

}
