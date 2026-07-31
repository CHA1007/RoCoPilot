namespace RocoPilot.Settings;

public enum AppTheme
{
    System,

    Light,

    Dark,
}

public sealed class ShellSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string AccentColor { get; set; } = "#0078D4";

    public string TakeoverHotkey { get; set; } = "F12";

    public bool DeveloperMode { get; set; } = false;

    public string CaptureBackend { get; set; } = "bitblt";

    public string InferenceDevice { get; set; } = "cpu";

    public int DetectionIntervalMs { get; set; }

    public bool DebugOverlay { get; set; }
}
