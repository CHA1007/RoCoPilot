using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Hotkeys;

public sealed class ShellHotkeys
{
    public const string CaptureToggleOwner = "CaptureToggle";

    private readonly GlobalHotkeyManager _manager;
    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;

    public ShellHotkeys(GlobalHotkeyManager manager, ISettingsStore store, CaptureHost capture)
    {
        _manager = manager;
        _store = store;
        _capture = capture;
    }

    public void Start()
    {
        _manager.Start();
        ApplyCaptureToggle();
    }

    public bool ApplyCaptureToggle()
    {
        var shell = _store.GetShellSettings();
        if (string.IsNullOrWhiteSpace(shell.CaptureToggleHotkey))
        {
            _manager.Unregister(CaptureToggleOwner);
            return true;
        }

        var result = _manager.Register(CaptureToggleOwner, shell.CaptureToggleHotkey, ToggleCapture);
        Trace.TraceInformation($"[ShellHotkeys] ApplyCaptureToggle hotkey='{shell.CaptureToggleHotkey}' result={result}");
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

    private static CaptureBackendMode ParseBackend(string key) =>
        Enum.TryParse<CaptureBackendMode>(key, ignoreCase: true, out var mode) ? mode : CaptureBackendMode.Auto;
}
