namespace RocoPilot.Capture;

public enum CaptureBackendMode
{
    Auto,

    ForceWgcWindow,

    ForceWgcMonitor,

    ForceGdi,
}

public sealed record CaptureOptions
{
    public string? WindowTitleSubstring { get; init; }

    public CaptureBackendMode Backend { get; init; } = CaptureBackendMode.Auto;

    public TimeSpan FirstFrameTimeout { get; init; } = CaptureDefaults.FirstFrameTimeout;

    public TimeSpan FpsWindow { get; init; } = CaptureDefaults.FpsWindow;
}
