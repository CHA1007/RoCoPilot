using System.Windows;
using System.Windows.Controls;
using RocoPilot.Capture;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class LaunchPage : Page
{
    private static readonly (string Key, CaptureBackendMode Mode)[] s_backends =
    [
        ("wgc", CaptureBackendMode.Wgc),
        ("wgc-hdr", CaptureBackendMode.WgcHdr),
        ("bitblt", CaptureBackendMode.BitBlt),
        ("dwm", CaptureBackendMode.DwmSharedSurface),
    ];

    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private bool _updating;

    public LaunchPage(ISettingsStore store, CaptureHost capture)
    {
        InitializeComponent();
        _store = store;
        _capture = capture;

        var shell = store.GetShellSettings();
        BackendCombo.SelectedIndex = Math.Max(0, Array.FindIndex(s_backends, b => b.Key == shell.CaptureBackend));
        DeviceCombo.SelectedIndex = string.Equals(shell.InferenceDevice, "gpu", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        IntervalBox.Value = shell.DetectionIntervalMs;

        Loaded += (_, _) =>
        {
            _capture.Changed += OnStateChanged;
            RefreshToggle();
        };
        Unloaded += (_, _) => _capture.Changed -= OnStateChanged;
    }

    private void OnCaptureToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (CaptureToggle.IsChecked == true)
        {
            _ = StartCaptureAsync();
        }
        else
        {
            _capture.Stop();
        }
    }

    private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || BackendCombo.SelectedIndex < 0) return;
        var shell = _store.GetShellSettings();
        shell.CaptureBackend = s_backends[BackendCombo.SelectedIndex].Key;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DeviceCombo.SelectedIndex < 0) return;
        var shell = _store.GetShellSettings();
        shell.InferenceDevice = DeviceCombo.SelectedIndex == 1 ? "gpu" : "cpu";
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnIntervalChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var shell = _store.GetShellSettings();
        shell.DetectionIntervalMs = (int)Math.Clamp(IntervalBox.Value ?? 0, 0, 5000);
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private async Task StartCaptureAsync()
    {
        var toolSettings = _store.GetToolSettings(
            AutoThrowTool.ToolId, typeof(AutoThrowSettings), () => new AutoThrowSettings());
        var title = ((AutoThrowSettings)toolSettings).WindowTitleSubstring;
        var backend = s_backends[Math.Max(0, BackendCombo.SelectedIndex)].Mode;
        if (!await _capture.StartAsync(title, backend))
        {
            Dispatcher.InvokeAsync(() =>
            {
                _updating = true;
                CaptureToggle.IsChecked = false;
                _updating = false;
            });
        }
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(RefreshToggle);

    private void RefreshToggle()
    {
        _updating = true;
        CaptureToggle.IsChecked = _capture.IsRunning;
        _updating = false;
    }
}
