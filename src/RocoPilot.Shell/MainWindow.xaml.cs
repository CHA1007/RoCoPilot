using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Hotkeys;
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
    private readonly CaptureHost _capture;
    private readonly ShellHotkeys _hotkeys;
    private readonly System.Windows.Threading.DispatcherTimer _brandTimer;
    private bool _brandEnglish = true;

    private void SwapBrand()
    {
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            _brandEnglish = !_brandEnglish;
            BrandText.Text = _brandEnglish ? "RocoPilot" : "洛克工具箱";
            BrandShift.Y = 5;
            BrandText.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            BrandShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        };
        BrandText.BeginAnimation(OpacityProperty, fade);
    }

    public MainWindow(
        INavigationViewPageProvider pageProvider,
        ISettingsStore store,
        RunningTaskHost taskHost,
        OverlayController overlay,
        CaptureHost capture,
        ShellHotkeys hotkeys)
    {
        InitializeComponent();

        _brandTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _brandTimer.Tick += (_, _) => SwapBrand();
        _brandTimer.Start();

        _store = store;
        _taskHost = taskHost;
        _overlay = overlay;
        _capture = capture;
        _hotkeys = hotkeys;
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

        Closed += (_, _) => _overlay.Shutdown();

        _hotkeys.Start();

        _overlay.Start();
    }

    private void BuildNavigation()
    {
        NavigationView.MenuItems.Add(new NavigationViewItem("启动", SymbolRegular.Rocket24, typeof(LaunchPage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("实时", SymbolRegular.TargetArrow24, typeof(RealtimePage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("孵蛋", SymbolRegular.FoodEgg24, typeof(EggQueryPage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("路线", SymbolRegular.Road24, typeof(RoutePage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("热键", SymbolRegular.Keyboard24, typeof(HotkeysPage)));

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

}
