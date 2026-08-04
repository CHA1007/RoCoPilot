using System.Windows;
using RocoPilot.Settings;
using Wpf.Ui.Appearance;

namespace RocoPilot.Shell.Appearance;

internal static class ShellTheme
{
    private static Window? _watched;

    public static void ApplyAndPersist(ISettingsStore store, AppTheme theme, Window? window)
    {
        var shell = store.GetShellSettings();
        shell.Theme = theme;
        store.SetShellSettings(shell);
        store.Save();

        Apply(theme);
        if (theme == AppTheme.System && window is not null)
        {
            WatchSystemTheme(window);
        }
    }

    public static bool FollowingSystem { get; private set; }

    public static void Apply(AppTheme theme)
    {
        FollowingSystem = theme == AppTheme.System;
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    public static void WatchSystemTheme(Window window)
    {
        if (ReferenceEquals(_watched, window))
        {
            return;
        }

        if (_watched is not null)
        {
            SystemThemeWatcher.UnWatch(_watched);
        }

        SystemThemeWatcher.Watch(window, Wpf.Ui.Controls.WindowBackdropType.None, updateAccents: false);
        _watched = window;
    }
}
