using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using RocoPilot.Capture;
using RocoPilot.Input.Interception;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;
using Wpf.Ui.Controls;

namespace RocoPilot.Shell.Pages;

public partial class LaunchPage : Page
{
    private static readonly string s_fallbackBanner =
        "pack://application:,,,/assets/banners/placeholder.png";

    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private readonly RocoBannerService _bannerService;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    public LaunchPage(ISettingsStore store, CaptureHost capture, RocoBannerService bannerService)
    {
        InitializeComponent();
        _store = store;
        _capture = capture;
        _bannerService = bannerService;

        foreach (var (_, label) in CaptureBackendCatalog.Choices)
        {
            BackendCombo.Items.Add(label);
        }

        var shell = store.GetShellSettings();
        BackendCombo.SelectedIndex = CaptureBackendCatalog.IndexOf(CaptureBackendCatalog.Parse(shell.CaptureBackend));
        SensitivityPpcXBox.Value = shell.SensitivityPpcX;
        SensitivityPpcYBox.Value = shell.SensitivityPpcY;

        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshCaptureStatus();

        Loaded += async (_, _) =>
        {
            _capture.Changed += OnStateChanged;
            RefreshButton();
            RefreshCaptureStatus();
            _statusTimer.Start();
            await LoadBannerAsync();
        };
        Unloaded += (_, _) =>
        {
            _capture.Changed -= OnStateChanged;
            _statusTimer.Stop();
        };
    }

    private async Task LoadBannerAsync()
    {
        var path = _bannerService.GetCachedPath() ?? await _bannerService.RefreshAsync();
        BannerImage.Source = new BitmapImage(new Uri(path ?? s_fallbackBanner));
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
        shell.CaptureBackend = CaptureBackendCatalog.Choices[BackendCombo.SelectedIndex].Mode.ToString();
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnSensitivityChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.SensitivityPpcX = SensitivityPpcXBox.Value ?? 0;
        shell.SensitivityPpcY = SensitivityPpcYBox.Value ?? 0;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        var source = _capture.CurrentSource;
        if (source is null)
        {
            CalibrateHint.Text = "请先启动截图器再校准";
            return;
        }

        CalibrateButton.IsEnabled = false;
        CalibrateHint.Text = "校准中…请保持游戏窗口聚焦，勿动鼠标";

        WindowFinder.ActivateGameWindow();

        try
        {
            var ppc = await Task.Run(() =>
            {
                using var driver = new InterceptionDriver();
                driver.Arm();
                return AutoCalibrator.Calibrate(source, driver);
            });

            if (ppc is { } result)
            {
                var shell = _store.GetShellSettings();
                shell.SensitivityPpcX = Math.Round(result.PpcX, 3);
                shell.SensitivityPpcY = Math.Round(result.PpcY, 3);
                _store.SetShellSettings(shell);
                _store.Save();
                SensitivityPpcXBox.Value = shell.SensitivityPpcX;
                SensitivityPpcYBox.Value = shell.SensitivityPpcY;
                CalibrateHint.Text = $"校准完成：X={result.PpcX:F3} Y={result.PpcY:F3}";
            }
            else
            {
                CalibrateHint.Text = "校准失败：未检测到足够的场景位移（场景纹理不足或镜头未转）";
            }
        }
        catch (Exception ex)
        {
            CalibrateHint.Text = $"校准异常：{ex.GetBaseException().Message}";
        }
        finally
        {
            CalibrateButton.IsEnabled = true;
        }
    }

    private async Task StartCaptureAsync()
    {
        var toolSettings = _store.GetToolSettings(
            AutoThrowTool.ToolId, typeof(AutoThrowSettings), () => new AutoThrowSettings());
        var title = ((AutoThrowSettings)toolSettings).WindowTitleSubstring;
        var backend = CaptureBackendCatalog.ModeAt(BackendCombo.SelectedIndex);
        if (!await _capture.StartAsync(title, backend))
        {
            _ = Dispatcher.InvokeAsync(RefreshButton);
        }
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(() =>
    {
        RefreshButton();
        RefreshCaptureStatus();
    });

    private void RefreshCaptureStatus()
    {
        var source = _capture.CurrentSource;
        if (source is null)
        {
            CaptureStatusRow.Visibility = Visibility.Collapsed;
            return;
        }

        CaptureStatusText.Text =
            $"{source.SourceDescription} · {source.FrameWidth}×{source.FrameHeight} · {source.FramesPerSecond:F0} FPS";
        CaptureStatusRow.Visibility = Visibility.Visible;
    }

    private void RefreshButton()
    {
        if (_capture.IsRunning)
        {
            CaptureButton.Content = "停止";
            CaptureButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 };
            CaptureButton.Appearance = ControlAppearance.Danger;
        }
        else
        {
            CaptureButton.Content = "启动";
            CaptureButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 };
            CaptureButton.Appearance = ControlAppearance.Primary;
        }
    }
}
