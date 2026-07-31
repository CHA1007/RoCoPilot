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

    /// <summary>水平视角灵敏度：每 count 对应的像素数。填 0 则用回退除数估算。</summary>
    public double SensitivityPpcX { get; set; }

    /// <summary>垂直视角灵敏度：每 count 对应的像素数。填 0 则用回退除数估算。</summary>
    public double SensitivityPpcY { get; set; }

    /// <summary>回退除数：SensitivityPpc 为 0 时，偏移像素 / 此值 = 转向 count 数。</summary>
    public double TurnFallbackDivisor { get; set; } = 4;

    /// <summary>垂直瞄准偏移：框高的比例，负值=往上。补偿检测框包含脚下阴影导致的偏下。</summary>
    public double AimOffsetY { get; set; } = -0.15;
}
