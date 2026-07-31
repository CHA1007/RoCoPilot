using System.Windows;
using System.Windows.Controls;
using RocoPilot.Input;
using RocoPilot.Input.Interception;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class SettingsPage : Page
{
    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private bool _suppressThemeEvent;

    public SettingsPage(ISettingsStore store, CaptureHost capture)
    {
        InitializeComponent();
        _store = store;
        _capture = capture;

        var shell = store.GetShellSettings();

        _suppressThemeEvent = true;
        ThemeCombo.SelectedIndex = (int)shell.Theme;
        _suppressThemeEvent = false;

        HotkeyText.Text = shell.TakeoverHotkey;
        PathsText.Text = $"设置：{store.FilePath}\n派生缓存：{RocoPaths.CacheDirectory}";
        DebugOverlayToggle.IsChecked = shell.DebugOverlay;
        SensitivityPpcXBox.Value = shell.SensitivityPpcX;
        SensitivityPpcYBox.Value = shell.SensitivityPpcY;
        FallbackDivisorBox.Value = shell.TurnFallbackDivisor;
        AimOffsetYBox.Value = shell.AimOffsetY;
    }

    private void OnDebugOverlayChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.DebugOverlay = DebugOverlayToggle.IsChecked == true;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnSensitivityChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.SensitivityPpcX = SensitivityPpcXBox.Value ?? 0;
        shell.SensitivityPpcY = SensitivityPpcYBox.Value ?? 0;
        shell.TurnFallbackDivisor = FallbackDivisorBox.Value ?? 4;
        shell.AimOffsetY = AimOffsetYBox.Value ?? -0.15;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        var source = _capture.CurrentSource;
        if (source is null)
        {
            CalibrateHint.Text = "请先开启截图（实时页 → 截图开关）";
            return;
        }

        CalibrateButton.IsEnabled = false;
        CalibrateHint.Text = "校准中…请保持游戏窗口聚焦，勿动鼠标";

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
                SensitivityPpcXBox.Value = shell.SensitivityPpcX;
                SensitivityPpcYBox.Value = shell.SensitivityPpcY;
                CalibrateHint.Text = $"校准完成：X={result.PpcX:F3} Y={result.PpcY:F3}（已保存）";
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

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeEvent || ThemeCombo.SelectedIndex < 0)
        {
            return;
        }

        ShellTheme.ApplyAndPersist(_store, (AppTheme)ThemeCombo.SelectedIndex, Window.GetWindow(this));
    }
}
