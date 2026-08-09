namespace RocoPilot.Settings;

public enum HotkeyScope
{
    Global,

    InGame,
}

public enum AppTheme
{
    System,

    Light,

    Dark,
}

public enum UpdateChannel
{
    Stable,

    Beta,
}

public sealed class ShellSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    public string AccentColor { get; set; } = "#0078D4";

    public bool DeveloperMode { get; set; } = false;

    public string CaptureBackend { get; set; } = "Auto";

    public string CaptureToggleHotkey { get; set; } = "F11";

    public string AutoThrowHotkey { get; set; } = "";

    public string AutoBattleHotkey { get; set; } = "";

    public string FastTravelHotkey { get; set; } = "";

    public string DebugOverlayHotkey { get; set; } = "";

    public HotkeyScope CaptureHotkeyScope { get; set; } = HotkeyScope.Global;

    public HotkeyScope AutoThrowHotkeyScope { get; set; } = HotkeyScope.Global;

    public HotkeyScope AutoBattleHotkeyScope { get; set; } = HotkeyScope.Global;

    public HotkeyScope FastTravelHotkeyScope { get; set; } = HotkeyScope.Global;

    public HotkeyScope DebugOverlayHotkeyScope { get; set; } = HotkeyScope.Global;

    public bool DebugOverlay { get; set; }

    public double SensitivityPpcX { get; set; } = 1.35;

    public double SensitivityPpcY { get; set; } = 0.333;

    public double TurnFallbackDivisor { get; set; } = 4;

    public double AimOffsetY { get; set; } = -0.15;

    public bool AutoThrowEnabled { get; set; }

    public bool AutoBattleEnabled { get; set; }

    public bool FastTravelEnabled { get; set; }

    public double WindowWidth { get; set; } = 900;

    public double WindowHeight { get; set; } = 600;

    public bool WindowMaximized { get; set; }
}
