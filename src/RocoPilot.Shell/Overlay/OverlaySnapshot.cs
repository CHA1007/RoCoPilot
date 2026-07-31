using RocoPilot.Core;

namespace RocoPilot.Shell.Overlay;

public sealed record OverlaySnapshot(
    TaskState State,
    int Throws,
    TimeSpan? SinceLastSettle,
    string? ArmingLine,
    string? FailureLine,
    string? StallBanner,
    bool CaptureRunning = false,
    string? Phase = null);
