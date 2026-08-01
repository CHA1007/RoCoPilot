using System.Windows;
using System.Windows.Controls;
using RocoPilot.Core;
using RocoPilot.Input.Interception;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;
using Wpf.Ui.Controls;

namespace RocoPilot.Shell.Pages;

public partial class RealtimePage : Page
{
    private readonly AutoThrowTool _tool;
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _taskHost;
    private readonly CaptureHost _capture;
    private readonly object _settings;
    private IRunningTask? _observed;

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

    private void OnAutoThrowClick(object sender, RoutedEventArgs e)
    {
        if (_taskHost.Current is not null)
        {
            _taskHost.RequestStop();
            return;
        }

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
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(() =>
    {
        Observe(_taskHost.Current);
        RefreshToggle();
    });

    private void Observe(IRunningTask? task)
    {
        if (ReferenceEquals(_observed, task)) return;
        if (_observed is not null)
        {
            _observed.EventRaised -= OnToolEvent;
        }

        _observed = task;
        if (task is not null)
        {
            task.EventRaised += OnToolEvent;
        }
        else
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnToolEvent(object? sender, ToolEvent toolEvent) => Dispatcher.InvokeAsync(() =>
    {
        switch (toolEvent.Name)
        {
            case "arming_step":
                StatusText.Text = "自检中（" + toolEvent.Data?["step"] + "）：" + toolEvent.Data?["hint"];
                StatusText.Visibility = Visibility.Visible;
                break;
            case "arming_failed":
                StatusText.Text = "启动失败（" + toolEvent.Data?["step"] + "）：" + toolEvent.Data?["error"] + "。" + toolEvent.Data?["remedy"];
                StatusText.Visibility = Visibility.Visible;
                break;
        }
    });

    private void RefreshToggle()
    {
        if (_taskHost.Current is not null)
        {
            AutoThrowButton.Content = "停止";
            AutoThrowButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 };
        }
        else
        {
            AutoThrowButton.Content = "启动";
            AutoThrowButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 };
        }
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

        // 校准前自动聚焦游戏窗口
        RocoPilot.Capture.WindowFinder.ActivateGameWindow();

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
