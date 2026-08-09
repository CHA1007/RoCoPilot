using RocoPilot.Capture;

namespace RocoPilot.Shell.Services;

public static class CaptureBackendCatalog
{
    public static readonly (CaptureBackendMode Mode, string Label)[] Choices =
    [
        (CaptureBackendMode.Auto, "自动"),
        (CaptureBackendMode.ForceWgcWindow, "窗口 Windows Graphics Capture"),
        (CaptureBackendMode.ForceWgcMonitor, "整屏 Windows Graphics Capture"),
        (CaptureBackendMode.ForceGdi, "BitBlt"),
    ];

    public static CaptureBackendMode Parse(string? key) => key switch
    {
        "bitblt" => CaptureBackendMode.ForceGdi,
        "wgc" => CaptureBackendMode.Auto,
        _ => Enum.TryParse<CaptureBackendMode>(key, true, out var mode) ? mode : CaptureBackendMode.Auto,
    };

    public static CaptureBackendMode ModeAt(int index) =>
        Choices[Math.Clamp(index, 0, Choices.Length - 1)].Mode;

    public static int IndexOf(CaptureBackendMode mode) =>
        Math.Max(0, Array.FindIndex(Choices, choice => choice.Mode == mode));
}
