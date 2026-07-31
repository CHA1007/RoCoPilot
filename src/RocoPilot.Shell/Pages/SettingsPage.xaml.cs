using System.Windows;
using System.Windows.Controls;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;

namespace RocoPilot.Shell.Pages;

public partial class SettingsPage : Page
{
    private readonly ISettingsStore _store;
    private bool _suppressThemeEvent;

    public SettingsPage(ISettingsStore store)
    {
        InitializeComponent();
        _store = store;

        var shell = store.GetShellSettings();

        _suppressThemeEvent = true;
        ThemeCombo.SelectedIndex = (int)shell.Theme;
        _suppressThemeEvent = false;

        HotkeyText.Text = shell.TakeoverHotkey;
        PathsText.Text = $"设置：{store.FilePath}\n派生缓存：{RocoPaths.CacheDirectory}";
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
