using System.Windows;
using System.Windows.Interop;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Overlay;
using RocoPilot.Shell.Pages;
using RocoPilot.Shell.Services;
using RocoPilot.Shell.Tools;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray.Controls;

namespace RocoPilot.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _taskHost;
    private readonly OverlayController _overlay;
    private HwndSource? _hwndSource;

    public MainWindow(
        INavigationViewPageProvider pageProvider,
        ISettingsStore store,
        RunningTaskHost taskHost,
        OverlayController overlay)
    {
        InitializeComponent();

        _store = store;
        _taskHost = taskHost;
        _overlay = overlay;
        NavigationView.SetPageProviderService(pageProvider);

        BuildNavigation();

        NavigationView.Loaded += (_, _) =>
        {
            if (NavigationView.SelectedItem is null)
            {
                NavigationView.Navigate(typeof(LaunchPage));
            }
        };

        Activated += (_, _) =>
        {
            if (ShellTheme.FollowingSystem && !ApplicationThemeManager.IsAppMatchesSystem())
            {
                ApplicationThemeManager.ApplySystemTheme();
            }
        };

        if (_store.GetShellSettings().Theme == AppTheme.System)
        {
            ShellTheme.WatchSystemTheme(this);
        }



        SourceInitialized += (_, _) =>
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
            if (_hwndSource is null ||
                !GlobalHotkey.RegisterHotKey(_hwndSource.Handle, GlobalHotkey.IdPauseToggle, fsModifiers: 0, GlobalHotkey.VkF12))
            {
                System.Diagnostics.Trace.TraceWarning("F12 全局热键注册失败（被其他程序占用？暂停 / 恢复将只有外壳按钮可用）");
            }
        };
        Closed += (_, _) =>
        {
            _overlay.Shutdown();
            if (_hwndSource is not null)
            {
                GlobalHotkey.UnregisterHotKey(_hwndSource.Handle, GlobalHotkey.IdPauseToggle);
                _hwndSource.RemoveHook(WndProc);
            }
        };

        _overlay.Start();
    }

    private void BuildNavigation()
    {
        NavigationView.MenuItems.Add(new NavigationViewItem("启动", SymbolRegular.Rocket24, typeof(LaunchPage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("实时", SymbolRegular.TargetArrow24, typeof(RealtimePage)));

        if (_store.GetShellSettings().DeveloperMode)
        {
            var diagnostics = new NavigationViewItem
            {
                Content = "诊断调试",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Pulse24 },
            };
            diagnostics.MenuItems.Add(new NavigationViewItem("输入探针", SymbolRegular.Keyboard24, typeof(InputProbePage)));
            diagnostics.MenuItems.Add(new NavigationViewItem("捕获调试", SymbolRegular.Screenshot24, typeof(CaptureDebugPage)));
            diagnostics.MenuItems.Add(new NavigationViewItem("检测调试", SymbolRegular.EyeTracking24, typeof(DetectionDebugPage)));
            diagnostics.MenuItems.Add(new NavigationViewItem("居中调试", SymbolRegular.AlignCenterHorizontal24, typeof(CenteringDebugPage)));
            diagnostics.MenuItems.Add(new NavigationViewItem("捕捉调试", SymbolRegular.AnimalRabbit24, typeof(CatchLoopDebugPage)));
            diagnostics.MenuItems.Add(new NavigationViewItem("失败现场", SymbolRegular.ImageMultiple24, typeof(ScenesPage)));
            NavigationView.MenuItems.Add(diagnostics);
        }

        NavigationView.FooterMenuItems.Add(new NavigationViewItem("设置", SymbolRegular.Settings24, typeof(SettingsPage)));
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        var current = _store.GetShellSettings().Theme;
        var next = (AppTheme)(((int)current + 1) % 3);
        ShellTheme.ApplyAndPersist(_store, next, this);
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e) => Hide();

    private void OnTrayLeftDoubleClick(NotifyIcon sender, RoutedEventArgs e) => RestoreFromTray();

    private void OnShowFromTrayClick(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkey.WmHotKey && wParam.ToInt32() == GlobalHotkey.IdPauseToggle)
        {
            _taskHost.TogglePauseResume();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
