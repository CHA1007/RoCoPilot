using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RocoPilot.Core;
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
    private readonly DispatcherHost _dispatcher;
    private readonly System.Windows.Threading.DispatcherTimer _brandTimer;
    private readonly DoubleAnimation _breath = new(1.0, 0.2, TimeSpan.FromMilliseconds(900))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };
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
        ShellHotkeys hotkeys,
        DispatcherHost dispatcher)
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
        _dispatcher = dispatcher;

        var shellSettings = _store.GetShellSettings();
        if (shellSettings.WindowWidth >= MinWidth && shellSettings.WindowHeight >= MinHeight)
        {
            Width = shellSettings.WindowWidth;
            Height = shellSettings.WindowHeight;
        }

        if (shellSettings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _dispatcher.Changed += OnDispatcherChanged;
        _dispatcher.TaskStateChanged += OnTaskStateChanged;
        UpdateStatusLight();
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

        Closed += (_, _) =>
        {
            _dispatcher.Changed -= OnDispatcherChanged;
            _dispatcher.TaskStateChanged -= OnTaskStateChanged;

            var shell = _store.GetShellSettings();
            shell.WindowMaximized = WindowState == WindowState.Maximized;
            var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, Width, Height) : RestoreBounds;
            shell.WindowWidth = bounds.Width;
            shell.WindowHeight = bounds.Height;
            _store.SetShellSettings(shell);
            _store.Save();

            _overlay.Shutdown();
        };

        _hotkeys.Start();

        _overlay.Start();
    }

    private void OnDispatcherChanged() => Dispatcher.InvokeAsync(UpdateStatusLight);

    private void OnTaskStateChanged(object? sender, TaskState state) => Dispatcher.InvokeAsync(UpdateStatusLight);

    private void UpdateStatusLight()
    {
        var (color, breathing, tip) = _dispatcher.DispatcherState switch
        {
            TaskState.Running => ("#6CCB5F", true, "任务运行中"),
            TaskState.Arming => ("#4CC2FF", true, "任务武装中"),
            TaskState.Paused => ("#FFB900", false, "任务已暂停"),
            TaskState.Stopping => ("#FF6B6B", false, "任务停止中"),
            _ => ("#8A8A8A", false, "任务待机"),
        };

        StatusLight.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StatusLight.ToolTip = tip;

        if (breathing)
        {
            StatusLight.BeginAnimation(OpacityProperty, _breath);
        }
        else
        {
            StatusLight.BeginAnimation(OpacityProperty, null);
            StatusLight.Opacity = 1;
        }
    }

    private void BuildNavigation()
    {
        NavigationView.MenuItems.Add(new NavigationViewItem("启动", SymbolRegular.Rocket24, typeof(LaunchPage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("实时触发", SymbolRegular.TargetArrow24, typeof(RealtimePage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("孵蛋", SymbolRegular.FoodEgg24, typeof(EggQueryPage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("流程", SymbolRegular.BranchFork24, typeof(RoutePage)));
        NavigationView.MenuItems.Add(new NavigationViewItem("热键", SymbolRegular.Keyboard24, typeof(HotkeysPage)));

        if (_store.GetShellSettings().DeveloperMode)
        {
            var diagnostics = new NavigationViewItem
            {
                Content = "诊断调试",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Pulse24 },
            };
            diagnostics.MenuItems.Add(new NavigationViewItem("管线调试", SymbolRegular.Pulse24, typeof(DiagnosticsPage)));
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

    private async void OnTrayCheckUpdateClick(object sender, RoutedEventArgs e) =>
        await UpdateFlow.CheckAsync(_store.GetShellSettings().UpdateChannel, _ => { });

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
