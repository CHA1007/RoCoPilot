using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Dialogs;
using RocoPilot.Shell.Hotkeys;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoBattle;
using RocoPilot.Tools.AutoThrow;
using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Shell.Pages;

public partial class RealtimePage : Page
{
    private const string ListeningTriggerKey = "按下按键…";

    private const string UnspecifiedTriggerKey = "未指定";

    private readonly AutoThrowTool _tool;
    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private readonly DispatcherHost _dispatcher;
    private readonly ShellHotkeys _hotkeys;
    private readonly object _throwSettings;
    private bool _ready;
    private bool _syncing;
    private bool _listeningTriggerKey;

    public RealtimePage(AutoThrowTool tool, ISettingsStore store, CaptureHost capture, DispatcherHost dispatcher, ShellHotkeys hotkeys)
    {
        InitializeComponent();

        _tool = tool;
        _store = store;
        _capture = capture;
        _dispatcher = dispatcher;
        _hotkeys = hotkeys;
        _throwSettings = store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);

        ConfigHost.Content = tool.CreateConfigPanel(_throwSettings, PersistThrow);

        var battleSettings = store.GetToolSettings(
            AutoBattleTool.Id, typeof(RocoPilot.Tools.AutoBattle.AutoBattleSettings),
            () => new RocoPilot.Tools.AutoBattle.AutoBattleSettings()) as RocoPilot.Tools.AutoBattle.AutoBattleSettings;
        SkillSlotCombo.SelectedIndex = Math.Clamp((battleSettings?.SkillSlot ?? 1) - 1, 0, 3);
        FastTravelModeCombo.SelectedIndex = (int)_dispatcher.FastTravelTriggerMode;
        UpdateTriggerKeyVisibility();
        FastTravelTriggerKeyButton.Content = DisplayTriggerKey(GetFastTravelSettings().TriggerKey);

        FastTravelTriggerKeyButton.Click += (_, _) =>
        {
            _listeningTriggerKey = true;
            FastTravelTriggerKeyButton.Content = ListeningTriggerKey;
            FastTravelTriggerKeyButton.Focus();
        };
        FastTravelTriggerKeyButton.PreviewKeyDown += OnTriggerKeyPreviewKeyDown;
        FastTravelTriggerKeyButton.LostFocus += (_, _) =>
        {
            if (!_listeningTriggerKey) return;
            _listeningTriggerKey = false;
            FastTravelTriggerKeyButton.Content = DisplayTriggerKey(GetFastTravelSettings().TriggerKey);
        };

        AutoThrowToggle.IsChecked = _dispatcher.AutoThrowEnabled;
        AutoBattleToggle.IsChecked = _dispatcher.AutoBattleEnabled;
        FastTravelToggle.IsChecked = _dispatcher.FastTravelEnabled;

        Loaded += (_, _) =>
        {
            _ready = true;
            SyncToggleChecks();
            _dispatcher.EventRaised += OnDispatcherEvent;
            _capture.AutoStartFailed += OnAutoStartFailed;
            RefreshCalibrationBanner();
        };
        Unloaded += (_, _) =>
        {
            _dispatcher.EventRaised -= OnDispatcherEvent;
            _capture.AutoStartFailed -= OnAutoStartFailed;
        };
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncing) return;

        _dispatcher.AutoThrowEnabled = AutoThrowToggle.IsChecked == true;
        _dispatcher.AutoBattleEnabled = AutoBattleToggle.IsChecked == true;
        _dispatcher.FastTravelEnabled = FastTravelToggle.IsChecked == true;
        _dispatcher.SyncEnables();

        SyncToggleChecks();
    }

    private void SyncToggleChecks()
    {
        _syncing = true;
        AutoThrowToggle.IsChecked = _dispatcher.AutoThrowEnabled;
        AutoBattleToggle.IsChecked = _dispatcher.AutoBattleEnabled;
        FastTravelToggle.IsChecked = _dispatcher.FastTravelEnabled;
        _syncing = false;
    }

    private void OnSkillSlotChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SkillSlotCombo.SelectedIndex < 0) return;
        var settings = _store.GetToolSettings(
            AutoBattleTool.Id, typeof(RocoPilot.Tools.AutoBattle.AutoBattleSettings),
            () => new RocoPilot.Tools.AutoBattle.AutoBattleSettings()) as RocoPilot.Tools.AutoBattle.AutoBattleSettings
                       ?? new RocoPilot.Tools.AutoBattle.AutoBattleSettings();
        settings.SkillSlot = SkillSlotCombo.SelectedIndex + 1;
        _store.SetToolSettings(AutoBattleTool.Id, settings);
        _store.Save();
    }

    private void OnFastTravelModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FastTravelModeCombo.SelectedIndex < 0) return;
        _dispatcher.FastTravelTriggerMode = (FastTravelTriggerMode)FastTravelModeCombo.SelectedIndex;
        UpdateTriggerKeyVisibility();
    }

    private void UpdateTriggerKeyVisibility()
    {
        var isKeyPress = _dispatcher.FastTravelTriggerMode == FastTravelTriggerMode.KeyPress;
        TriggerKeyRow.Visibility = isKeyPress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTriggerKeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_listeningTriggerKey) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed
            or Key.Back or Key.Delete)
        {
            return;
        }

        if (key == Key.Escape)
        {
            var cleared = GetFastTravelSettings();
            cleared.TriggerKey = string.Empty;
            _store.SetToolSettings(FastTravelTool.Id, cleared);
            _store.Save();
            _hotkeys.ApplyFastTravelTrigger();

            _listeningTriggerKey = false;
            FastTravelTriggerKeyButton.Content = UnspecifiedTriggerKey;
            return;
        }

        var triggerKey = HotkeyBinding.Format(key, Keyboard.Modifiers);

        var settings = GetFastTravelSettings();
        settings.TriggerKey = triggerKey;
        _store.SetToolSettings(FastTravelTool.Id, settings);
        _store.Save();
        _hotkeys.ApplyFastTravelTrigger();

        _listeningTriggerKey = false;
        FastTravelTriggerKeyButton.Content = DisplayTriggerKey(triggerKey);
    }

    private FastTravelSettings GetFastTravelSettings() =>
        _store.GetToolSettings(
            FastTravelTool.Id, typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
        ?? new FastTravelSettings();

    private static string DisplayTriggerKey(string triggerKey) =>
        string.IsNullOrWhiteSpace(triggerKey) ? UnspecifiedTriggerKey : triggerKey;

    private void OnAutoStartFailed(string reason)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ErrorBannerText.Text = reason;
            PickWindowButton.Visibility = Visibility.Visible;
            ErrorBanner.Visibility = Visibility.Visible;
        });
    }

    private void OnPickWindowClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Picked is not { } picked)
        {
            return;
        }

        _capture.WindowTitleSubstring = picked.Title;
        _capture.RetryStart();
    }

    private void OnDispatcherEvent(object? sender, ToolEvent toolEvent) => Dispatcher.InvokeAsync(() =>
    {
        switch (toolEvent.Name)
        {
            case "arming_failed":
                ErrorBannerText.Text = "启动失败：" + toolEvent.Data?["error"] + "。" + toolEvent.Data?["remedy"];
                ErrorBanner.Visibility = Visibility.Visible;
                break;

            case "fault":
                ErrorBannerText.Text = "异常：" + toolEvent.Data?["error"];
                ErrorBanner.Visibility = Visibility.Visible;
                break;
        }
    });

    private void PersistThrow()
    {
        _store.SetToolSettings(_tool.Id, _throwSettings);
        _store.Save();
    }

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
        using var lease = _capture.Acquire("Calibrate");
        await _capture.EnsureStartAsync();

        var source = _capture.CurrentSource;
        if (source is null)
        {
            CalibrationHint.Text = "未检测到游戏窗口，无法校准";
            return;
        }

        CalibrateButton.IsEnabled = false;
        CalibrationHint.Text = "校准中…勿动鼠标";

        WindowFinder.ActivateGameWindow();

        try
        {
            var ppc = await Task.Run(() =>
            {
                using var driver = new RocoPilot.Input.Interception.InterceptionDriver();
                driver.Arm();
                return RocoPilot.Loop.AutoCalibrator.Calibrate(source, driver);
            });

            if (ppc is { } result)
            {
                var shell = _store.GetShellSettings();
                shell.SensitivityPpcX = Math.Round(result.PpcX, 3);
                shell.SensitivityPpcY = Math.Round(result.PpcY, 3);
                _store.SetShellSettings(shell);
                _store.Save();
                CalibrationHint.Text = $"校准完成：X={result.PpcX:F3} Y={result.PpcY:F3}";
                CalibrationBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                CalibrationHint.Text = "校准失败：场景位移不足";
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
