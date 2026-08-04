using System.Windows;
using System.Windows.Controls;
using RocoPilot.Capture;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;
using Wpf.Ui.Controls;

namespace RocoPilot.Shell.Pages;

public partial class LaunchPage : Page
{
    private static readonly (string Key, CaptureBackendMode Mode)[] s_backends =
    [
        ("bitblt", CaptureBackendMode.BitBlt),
        ("wgc", CaptureBackendMode.Wgc),
        ("wgc-hdr", CaptureBackendMode.WgcHdr),
        ("dwm", CaptureBackendMode.DwmSharedSurface),
    ];

    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;

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
            RefreshButton();
        };
        Unloaded += (_, _) => _capture.Changed -= OnStateChanged;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (_capture.IsRunning)
        {
            _capture.Stop();
        }
        else
        {
            _ = StartCaptureAsync();
        }
    }

    private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackendCombo.SelectedIndex < 0) return;
        var shell = _store.GetShellSettings();
        shell.CaptureBackend = s_backends[BackendCombo.SelectedIndex].Key;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceCombo.SelectedIndex < 0) return;
        var shell = _store.GetShellSettings();
        shell.InferenceDevice = DeviceCombo.SelectedIndex == 1 ? "gpu" : "cpu";
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnIntervalChanged(object sender, RoutedEventArgs e)
    {
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
            _ = Dispatcher.InvokeAsync(RefreshButton);
        }
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(RefreshButton);

    private void RefreshButton()
    {
        if (_capture.IsRunning)
        {
            CaptureButton.Content = "停止";
            CaptureButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 };
        }
        else
        {
            CaptureButton.Content = "启动";
            CaptureButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 };
        }
    }
}
