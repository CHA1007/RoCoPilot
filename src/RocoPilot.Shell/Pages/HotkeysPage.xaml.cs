using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RocoPilot.Settings;
using RocoPilot.Shell.Hotkeys;

namespace RocoPilot.Shell.Pages;

public partial class HotkeysPage : Page
{
    private const string Listening = "";

    private const string Unspecified = "未指定";

    private readonly ISettingsStore _store;
    private readonly ShellHotkeys _hotkeys;
    private readonly HashSet<Button> _listening = [];

    public HotkeysPage(ISettingsStore store, ShellHotkeys hotkeys)
    {
        InitializeComponent();
        _store = store;
        _hotkeys = hotkeys;

        AttachScope(CaptureScopeBox,
            s => s.CaptureHotkeyScope, (s, v) => s.CaptureHotkeyScope = v,
            () => _hotkeys.ApplyCaptureToggle());
        AttachScope(AutoThrowScopeBox,
            s => s.AutoThrowHotkeyScope, (s, v) => s.AutoThrowHotkeyScope = v,
            () => _hotkeys.ApplyAutoThrowToggle());
        AttachScope(AutoBattleScopeBox,
            s => s.AutoBattleHotkeyScope, (s, v) => s.AutoBattleHotkeyScope = v,
            () => _hotkeys.ApplyAutoBattleToggle());
        AttachScope(FastTravelScopeBox,
            s => s.FastTravelHotkeyScope, (s, v) => s.FastTravelHotkeyScope = v,
            () => _hotkeys.ApplyFastTravelToggle());
        AttachScope(DebugOverlayScopeBox,
            s => s.DebugOverlayHotkeyScope, (s, v) => s.DebugOverlayHotkeyScope = v,
            () => _hotkeys.ApplyDebugOverlayToggle());

        AttachBinding(CaptureToggleButton,
            s => s.CaptureToggleHotkey, (s, v) => s.CaptureToggleHotkey = v,
            () => _hotkeys.ApplyCaptureToggle());
        AttachBinding(AutoThrowToggleButton,
            s => s.AutoThrowHotkey, (s, v) => s.AutoThrowHotkey = v,
            () => _hotkeys.ApplyAutoThrowToggle());
        AttachBinding(AutoBattleToggleButton,
            s => s.AutoBattleHotkey, (s, v) => s.AutoBattleHotkey = v,
            () => _hotkeys.ApplyAutoBattleToggle());
        AttachBinding(FastTravelToggleButton,
            s => s.FastTravelHotkey, (s, v) => s.FastTravelHotkey = v,
            () => _hotkeys.ApplyFastTravelToggle());
        AttachBinding(DebugOverlayToggleButton,
            s => s.DebugOverlayHotkey, (s, v) => s.DebugOverlayHotkey = v,
            () => _hotkeys.ApplyDebugOverlayToggle());

        Loaded += (_, _) => _hotkeys.ApplyAll();
    }

    private void AttachScope(
        ComboBox scopeBox,
        Func<ShellSettings, HotkeyScope> getter,
        Action<ShellSettings, HotkeyScope> setter,
        Action apply)
    {
        scopeBox.SelectedIndex = (int)getter(_store.GetShellSettings());
        scopeBox.SelectionChanged += (_, _) =>
        {
            if (scopeBox.SelectedIndex < 0) return;

            var shell = _store.GetShellSettings();
            setter(shell, (HotkeyScope)scopeBox.SelectedIndex);
            _store.SetShellSettings(shell);
            _store.Save();
            apply();
        };
    }

    private void AttachBinding(
        Button bindButton,
        Func<ShellSettings, string> getter,
        Action<ShellSettings, string> setter,
        Action apply)
    {
        bindButton.Content = Display(getter(_store.GetShellSettings()));

        bindButton.Click += (_, _) =>
        {
            _listening.Add(bindButton);
            bindButton.Content = Listening;
            bindButton.Focus();
        };

        bindButton.PreviewKeyDown += (_, e) =>
        {
            if (!_listening.Contains(bindButton)) return;
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
                var cleared = _store.GetShellSettings();
                setter(cleared, string.Empty);
                _store.SetShellSettings(cleared);
                _store.Save();

                bindButton.Content = Unspecified;
                _listening.Remove(bindButton);
                apply();
                return;
            }

            var hotkey = HotkeyBinding.Format(key, Keyboard.Modifiers);
            var shell = _store.GetShellSettings();
            setter(shell, hotkey);
            _store.SetShellSettings(shell);
            _store.Save();

            bindButton.Content = Display(hotkey);
            _listening.Remove(bindButton);
            apply();
        };

        bindButton.LostFocus += (_, _) =>
        {
            if (!_listening.Remove(bindButton)) return;
            bindButton.Content = Display(getter(_store.GetShellSettings()));
        };

    }

    private static string Display(string hotkey) =>
        string.IsNullOrWhiteSpace(hotkey) ? Unspecified : hotkey;
}