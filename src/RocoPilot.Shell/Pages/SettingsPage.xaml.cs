using System.Windows;
using System.Windows.Controls;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class SettingsPage : Page
{
    private readonly ISettingsStore _store;
    private bool _suppressThemeEvent;
    private bool _suppressChannelEvent;

    private const string IssuesUrl = "https://github.com/CHA1007/RoCoPilot/issues";

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        AboutCard.IsExpanded = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Visibility = Visibility.Visible;

        var channel = _store.GetShellSettings().UpdateChannel;
        await UpdateFlow.CheckAsync(channel, text => UpdateStatusText.Text = text);

        CheckUpdateButton.IsEnabled = true;
    }

    private void OnUpdateChannelChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressChannelEvent || UpdateChannelCombo.SelectedIndex < 0)
        {
            return;
        }

        var shell = _store.GetShellSettings();
        shell.UpdateChannel = (UpdateChannel)UpdateChannelCombo.SelectedIndex;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnOpenIssuesClick(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(IssuesUrl) { UseShellExecute = true });

    public SettingsPage(ISettingsStore store)
    {
        InitializeComponent();

        VersionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        _store = store;

        var shell = store.GetShellSettings();

        _suppressThemeEvent = true;
        ThemeCombo.SelectedIndex = (int)shell.Theme;
        _suppressThemeEvent = false;

        DebugOverlayToggle.IsChecked = shell.DebugOverlay;
        DeveloperModeToggle.IsChecked = shell.DeveloperMode;

        _suppressChannelEvent = true;
        UpdateChannelCombo.SelectedIndex = (int)shell.UpdateChannel;
        _suppressChannelEvent = false;
    }

    private void OnDebugOverlayChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.DebugOverlay = DebugOverlayToggle.IsChecked == true;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnDeveloperModeChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.DeveloperMode = DeveloperModeToggle.IsChecked == true;
        _store.SetShellSettings(shell);
        _store.Save();
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
