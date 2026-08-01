using System.Windows;
using System.Windows.Controls;
using RocoPilot.Input.Interception;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class RealtimePage : Page
{
    private readonly AutoThrowTool _tool;
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _taskHost;
    private readonly CaptureHost _capture;
    private readonly object _settings;
    private bool _updating;

    public RealtimePage(AutoThrowTool tool, ISettingsStore store, RunningTaskHost taskHost, CaptureHost capture)
    {
        InitializeComponent();

        _tool = tool;
        _store = store;
        _taskHost = taskHost;
        _capture = capture;
        _settings = store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);
        ConfigHost.Content = tool.CreateConfigPanel(_settings, Persist);

        Loaded += (_, _) =>
        {
            _taskHost.Changed += OnStateChanged;
            RefreshToggle();
            RefreshCalibrationBanner();
        };
        Unloaded += (_, _) => _taskHost.Changed -= OnStateChanged;
    }

    private void OnAutoThrowToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (AutoThrowToggle.IsChecked == true)
        {
            ((AutoThrowSettings)_settings).InferenceDevice = _store.GetShellSettings().InferenceDevice;
            ((AutoThrowSettings)_settings).DetectionIntervalMs = _store.GetShellSettings().DetectionIntervalMs;
            if (_taskHost.TryStart(_tool, _settings))
            {
                if (!_capture.IsRunning)
                {
                    var title = ((AutoThrowSettings)_settings).WindowTitleSubstring;
                    _ = _capture.StartAsync(title);
                }
            }
            else
            {
                _updating = true;
                AutoThrowToggle.IsChecked = false;
                _updating = false;
            }
        }
        else
        {
            _taskHost.RequestStop();
        }
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(RefreshToggle);

    private void RefreshToggle()
    {
        _updating = true;
        AutoThrowToggle.IsChecked = _taskHost.Current is not null;
        _updating = false;
    }

    private void Persist()
    {
        _store.SetToolSettings(_tool.Id, _settings);
        _store.Save();
    }

    // ── 灵敏度校准提醒 ──

    private void RefreshCalibrationBanner()
    {
        var shell = _store.GetShellSettings();
        CalibrationBanner.Visibility =
            shell.SensitivityPpcX <= 0 && shell.SensitivityPpcY <= 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        var source = _capture.CurrentSource;
        if (source is null)
        {
            CalibrationHint.Text = "请先开启截图（启动页 → 截图器开关）";
            return;
        }

        CalibrateButton.IsEnabled = false;
        CalibrationHint.Text = "校准中…请保持游戏窗口聚焦，勿动鼠标";

        try
        {
            var ppc = await Task.Run(() =>
            {
                using var driver = new InterceptionDriver();
                driver.Arm(TimeSpan.FromSeconds(5));
                return AutoCalibrator.Calibrate(source, driver);
            });

            if (ppc is { } result)
            {
                var shell = _store.GetShellSettings();
                shell.SensitivityPpcX = Math.Round(result.PpcX, 3);
                shell.SensitivityPpcY = Math.Round(result.PpcY, 3);
                _store.SetShellSettings(shell);
                _store.Save();
                CalibrationHint.Text = $"校准完成：X={result.PpcX:F3} Y={result.PpcY:F3}（已保存）";
                CalibrationBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                CalibrationHint.Text = "校准失败：未检测到足够的场景位移（场景纹理不足或镜头未转）";
            }
        }
        catch (Exception ex)
        {
            CalibrationHint.Text = $"校准异常：{ex.GetBaseException().Message}";
        }
        finally
        {
            CalibrateButton.IsEnabled = true;
        }
    }
}
