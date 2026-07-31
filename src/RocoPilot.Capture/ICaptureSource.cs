namespace RocoPilot.Capture;

public interface ICaptureSource : IDisposable
{
    string BackendName { get; }

    string SourceDescription { get; }

    int FrameWidth { get; }

    int FrameHeight { get; }

    double FramesPerSecond { get; }

    long FramesDelivered { get; }

    event EventHandler? FrameArrived;

    event EventHandler<CaptureStoppedEventArgs>? Stopped;

    Task StartAsync(CancellationToken cancellationToken = default);

    void Stop();

    bool TryGrabLatest(out CapturedFrame? frame);
}

public static class CaptureBackends
{
    public const string WgcWindow = "wgc-window";

    public const string WgcMonitor = "wgc-monitor";

    public const string GdiMonitor = "gdi-monitor";
}

public sealed record CaptureStoppedEventArgs(string Reason, Exception? Error = null);
