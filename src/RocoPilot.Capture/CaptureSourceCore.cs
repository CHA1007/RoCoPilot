using System.Buffers;

namespace RocoPilot.Capture;

public abstract class CaptureSourceCore : ICaptureSource
{
    private readonly object _gate = new();
    private readonly RollingFpsMeter _meter;

    private byte[]? _latest;
    private int _latestWidth;
    private int _latestHeight;
    private DateTimeOffset _latestCapturedAt;
    private long _sequence;
    private long _framesDelivered;
    private int _stoppedRaised;
    private int _disposed;

    protected CaptureSourceCore(string backendName, string sourceDescription, TimeSpan? fpsWindow = null)
    {
        BackendName = backendName;
        SourceDescription = sourceDescription;
        _meter = new RollingFpsMeter(fpsWindow ?? CaptureDefaults.FpsWindow);
    }

    public string BackendName { get; }

    public string SourceDescription { get; }

    public int FrameWidth { get { lock (_gate) { return _latestWidth; } } }

    public int FrameHeight { get { lock (_gate) { return _latestHeight; } } }

    public double FramesPerSecond => _meter.CurrentFps;

    public long FramesDelivered => Interlocked.Read(ref _framesDelivered);

    public event EventHandler? FrameArrived;

    public event EventHandler<CaptureStoppedEventArgs>? Stopped;

    public abstract Task StartAsync(CancellationToken cancellationToken = default);

    public abstract void Stop();

    public bool TryGrabLatest(out CapturedFrame? frame)
    {
        lock (_gate)
        {
            if (_latest is null)
            {
                frame = null;
                return false;
            }

            var length = _latestWidth * _latestHeight * 4;
            var copy = ArrayPool<byte>.Shared.Rent(length);
            Array.Copy(_latest, copy, length);
            frame = new CapturedFrame(copy, _latestWidth, _latestHeight, _sequence, _latestCapturedAt);
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        ReleaseBackend();
        lock (_gate)
        {
            if (_latest is not null)
            {
                ArrayPool<byte>.Shared.Return(_latest);
                _latest = null;
            }
        }
    }

    protected void PublishFrame(byte[] rented, int width, int height)
    {
        var capturedAt = DateTimeOffset.Now;
        lock (_gate)
        {
            var previous = _latest;
            _latest = rented;
            _latestWidth = width;
            _latestHeight = height;
            _latestCapturedAt = capturedAt;
            _sequence++;
            if (previous is not null)
            {
                ArrayPool<byte>.Shared.Return(previous);
            }
        }

        Interlocked.Increment(ref _framesDelivered);
        _meter.Tick();
        FrameArrived?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseStopped(string reason, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _stoppedRaised, 1) != 0)
        {
            return;
        }

        Stopped?.Invoke(this, new CaptureStoppedEventArgs(reason, error));
    }

    protected void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    protected abstract void ReleaseBackend();
}

public static class CaptureDefaults
{
    public static TimeSpan FpsWindow { get; } = TimeSpan.FromSeconds(10);

    public static TimeSpan FirstFrameTimeout { get; } = TimeSpan.FromSeconds(5);
}
