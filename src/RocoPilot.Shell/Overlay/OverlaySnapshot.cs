using RocoPilot.Core;

namespace RocoPilot.Shell.Overlay;

public sealed record OverlaySnapshot(
    TaskState State,
    int Throws,
    string? StallBanner,
    bool CaptureRunning = false,
    string? Phase = null,
    string? Scene = null);
