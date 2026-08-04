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

    public bool DeveloperMode { get; set; } = false;

    public string CaptureBackend { get; set; } = "wgc";

    public bool DebugOverlay { get; set; }

    public double SensitivityPpcX { get; set; } = 1.35;

    public double SensitivityPpcY { get; set; } = 0.333;

    public double TurnFallbackDivisor { get; set; } = 4;

    public double AimOffsetY { get; set; } = -0.15;

    public bool AutoThrowEnabled { get; set; }

    public bool AutoBattleEnabled { get; set; }

    public bool FastTravelEnabled { get; set; }
}
