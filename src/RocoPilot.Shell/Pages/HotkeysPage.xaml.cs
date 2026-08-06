using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RocoPilot.Settings;
using RocoPilot.Shell.Hotkeys;

namespace RocoPilot.Shell.Pages;

public partial class HotkeysPage : Page
{
    private const string CapturePrompt = "请按下绑定按键";

    private readonly ISettingsStore _store;
    private readonly ShellHotkeys _hotkeys;

    public HotkeysPage(ISettingsStore store, ShellHotkeys hotkeys)
    {
        InitializeComponent();
        _store = store;
        _hotkeys = hotkeys;
        CaptureToggleBox.Text = store.GetShellSettings().CaptureToggleHotkey;
        Loaded += (_, _) => _hotkeys.ApplyCaptureToggle();
    }

    private void OnCaptureToggleGotFocus(object sender, RoutedEventArgs e) =>
        CaptureToggleBox.Text = CapturePrompt;

    private void OnCaptureToggleLostFocus(object sender, RoutedEventArgs e) =>
        CaptureToggleBox.Text = _store.GetShellSettings().CaptureToggleHotkey;

    private void OnCaptureToggleKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed)
        {
            return;
        }

        var hotkey = key == Key.Escape ? string.Empty : HotkeyBinding.Format(key, Keyboard.Modifiers);
        CaptureToggleBox.Text = hotkey;

        var shell = _store.GetShellSettings();
        shell.CaptureToggleHotkey = hotkey;
        _store.SetShellSettings(shell);
        _store.Save();

        _hotkeys.ApplyCaptureToggle();
    }
}
