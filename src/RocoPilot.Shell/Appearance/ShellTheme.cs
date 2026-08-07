using System.Windows;
using System.Windows.Media;
using RocoPilot.Settings;
using Wpf.Ui.Appearance;

namespace RocoPilot.Shell.Appearance;

internal static class ShellTheme
{
    private static Window? _watched;
    private static bool _themeChangedHooked;

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

        SyncCardContrast();
    }

    private static void SyncCardContrast()
    {
        if (!_themeChangedHooked)
        {
            ApplicationThemeManager.Changed += (_, _) => ApplyCardContrast();
            _themeChangedHooked = true;
        }

        ApplyCardContrast();
    }

    private static void ApplyCardContrast()
    {
        // 玻璃拟态的资源覆盖已由 Appearance/GlassTheme.xaml 声明（MergedDictionaries 末尾，优先级最高）。
    }

    private static SolidColorBrush FrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
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

        SystemThemeWatcher.Watch(window, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccents: false);
        _watched = window;
    }
}
