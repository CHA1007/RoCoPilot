using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;
using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Shell.Hotkeys;

public sealed class ShellHotkeys
{
    public const string CaptureToggleOwner = "CaptureToggle";

    public const string AutoThrowToggleOwner = "AutoThrowToggle";

    public const string AutoBattleToggleOwner = "AutoBattleToggle";

    public const string FastTravelToggleOwner = "FastTravelToggle";

    public const string FastTravelTriggerOwner = "FastTravelTrigger";

    public const string DebugOverlayToggleOwner = "DebugOverlayToggle";

    private readonly GlobalHotkeyManager _manager;
    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private readonly DispatcherHost _dispatcher;

    public ShellHotkeys(GlobalHotkeyManager manager, ISettingsStore store, CaptureHost capture, DispatcherHost dispatcher)
    {
        _manager = manager;
        _store = store;
        _capture = capture;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _manager.Start();
        ApplyAll();
    }

    public void ApplyAll()
    {
        var shell = _store.GetShellSettings();
        RegisterOrClear(CaptureToggleOwner, shell.CaptureToggleHotkey, shell.CaptureHotkeyScope, ToggleCapture);
        RegisterOrClear(AutoThrowToggleOwner, shell.AutoThrowHotkey, shell.AutoThrowHotkeyScope, ToggleAutoThrow);
        RegisterOrClear(AutoBattleToggleOwner, shell.AutoBattleHotkey, shell.AutoBattleHotkeyScope, ToggleAutoBattle);
        RegisterOrClear(FastTravelToggleOwner, shell.FastTravelHotkey, shell.FastTravelHotkeyScope, ToggleFastTravel);
        ApplyFastTravelTrigger();
        RegisterOrClear(DebugOverlayToggleOwner, shell.DebugOverlayHotkey, shell.DebugOverlayHotkeyScope, ToggleDebugOverlay);
    }

    public bool ApplyCaptureToggle()
    {
        var shell = _store.GetShellSettings();
        return RegisterOrClear(CaptureToggleOwner, shell.CaptureToggleHotkey, shell.CaptureHotkeyScope, ToggleCapture);
    }

    public bool ApplyAutoThrowToggle()
    {
        var shell = _store.GetShellSettings();
        return RegisterOrClear(AutoThrowToggleOwner, shell.AutoThrowHotkey, shell.AutoThrowHotkeyScope, ToggleAutoThrow);
    }

    public bool ApplyAutoBattleToggle()
    {
        var shell = _store.GetShellSettings();
        return RegisterOrClear(AutoBattleToggleOwner, shell.AutoBattleHotkey, shell.AutoBattleHotkeyScope, ToggleAutoBattle);
    }

    public bool ApplyFastTravelToggle()
    {
        var shell = _store.GetShellSettings();
        return RegisterOrClear(FastTravelToggleOwner, shell.FastTravelHotkey, shell.FastTravelHotkeyScope, ToggleFastTravel);
    }

    public bool ApplyFastTravelTrigger()
    {
        var settings = _store.GetToolSettings(
            "fast-travel", typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
                       ?? new FastTravelSettings();
        return RegisterOrClear(FastTravelTriggerOwner, settings.TriggerKey, HotkeyScope.Global, TriggerFastTravel);
    }

    public bool ApplyDebugOverlayToggle()
    {
        var shell = _store.GetShellSettings();
        return RegisterOrClear(DebugOverlayToggleOwner, shell.DebugOverlayHotkey, shell.DebugOverlayHotkeyScope, ToggleDebugOverlay);
    }

    private bool RegisterOrClear(string owner, string hotkey, HotkeyScope scope, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            _manager.Unregister(owner);
            return true;
        }

        var result = _manager.Register(owner, hotkey, scope, callback);
        Trace.TraceInformation($"[ShellHotkeys] Register owner={owner} hotkey='{hotkey}' result={result}");
        return result;
    }

    private void ToggleCapture()
    {
        Trace.TraceInformation($"[ShellHotkeys] ToggleCapture fired, running={_capture.IsRunning}");
        if (_capture.IsRunning)
        {
            _capture.Stop();
            return;
        }

        var shell = _store.GetShellSettings();
        var toolSettings = _store.GetToolSettings(
            AutoThrowTool.ToolId, typeof(AutoThrowSettings), () => new AutoThrowSettings());
        var title = ((AutoThrowSettings)toolSettings).WindowTitleSubstring;
        _ = _capture.StartAsync(title, ParseBackend(shell.CaptureBackend));
    }

    private void ToggleAutoThrow() => _dispatcher.AutoThrowEnabled = !_dispatcher.AutoThrowEnabled;

    private void ToggleAutoBattle() => _dispatcher.AutoBattleEnabled = !_dispatcher.AutoBattleEnabled;

    private void ToggleFastTravel() => _dispatcher.FastTravelEnabled = !_dispatcher.FastTravelEnabled;

    private void TriggerFastTravel() => _dispatcher.TriggerFastTravel();

    private void ToggleDebugOverlay()
    {
        var shell = _store.GetShellSettings();
        shell.DebugOverlay = !shell.DebugOverlay;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private static CaptureBackendMode ParseBackend(string key) =>
        Enum.TryParse<CaptureBackendMode>(key, ignoreCase: true, out var mode) ? mode : CaptureBackendMode.Auto;
}