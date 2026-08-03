using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RocoPilot.Core;

namespace RocoPilot.Shell.Overlay;

public partial class OverlayWindow : Window
{

    private const double CompactWidth = 200, CompactHeight = 36, CompactRadius = 18;
    private const double ExpandedWidth = 300, ExpandedHeight = 72, ExpandedRadius = 36;

    private static readonly TimeSpan MorphDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan SceneExpandWindow = TimeSpan.FromMilliseconds(2200);

    private static readonly SolidColorBrush s_accentRunning = Frozen("#FF22C55E");
    private static readonly SolidColorBrush s_accentPaused = Frozen("#FFEF4444");
    private static readonly SolidColorBrush s_accentArming = Frozen("#FF3B82F6");
    private static readonly SolidColorBrush s_accentNeutral = Frozen("#FF94A3B8");

    private static readonly SolidColorBrush s_sceneWorld = Frozen("#FF86EFAC");
    private static readonly SolidColorBrush s_sceneBattle = Frozen("#FFFDA4AF");
    private static readonly SolidColorBrush s_sceneMap = Frozen("#FF93C5FD");
    private static readonly SolidColorBrush s_sceneUnknown = Frozen("#99FFFFFF");

    private static readonly SolidColorBrush s_stallYellow = Frozen("#FFD60A");
    private static readonly SolidColorBrush s_borderStall = Frozen("#66FFD60A");
    private static readonly SolidColorBrush s_borderIdle = Frozen("#14FFFFFF");

    private readonly DispatcherTimer _shrinkTimer;

    private Storyboard? _pulse;
    private bool _expanded;
    private bool _sceneExpandActive;
    private string? _lastPhase;
    private string? _lastScene;
    private string? _expandScene;
    private bool _expandLeaving;
    private int _lastThrows;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();

        _shrinkTimer = new DispatcherTimer { Interval = SceneExpandWindow };
        _shrinkTimer.Tick += (_, _) =>
        {
            _shrinkTimer.Stop();
            _sceneExpandActive = false;
            MorphTo(false);
        };
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayNative.SetExStyle(
            hwnd,
            OverlayNative.GetExStyle(hwnd) | OverlayNative.WsExTransparent | OverlayNative.WsExToolWindow | OverlayNative.WsExNoActivate);
    }

    internal void Apply(OverlaySnapshot snapshot)
    {
        StateDot.Fill = AccentFor(snapshot.State);
        SyncDotPulse(snapshot.State == TaskState.Running);

        if (!string.Equals(snapshot.Scene, _lastScene, StringComparison.Ordinal))
        {
            var previous = _lastScene;
            _lastScene = snapshot.Scene;
            var entering = IsKnownScene(snapshot.Scene);
            var leaving = !entering && IsKnownScene(previous);
            if (entering || leaving)
            {
                _expandScene = entering ? snapshot.Scene : previous;
                _expandLeaving = leaving;
                _sceneExpandActive = true;
                _shrinkTimer.Stop();
                _shrinkTimer.Start();
            }
        }

        SceneText.Text = SceneLabel(snapshot.Scene);
        SceneText.Foreground = SceneBrush(snapshot.Scene);

        var phase = snapshot.Phase;
        if (!string.Equals(phase, _lastPhase, StringComparison.Ordinal))
        {
            _lastPhase = phase;
            PhaseText.Text = phase ?? "待机";
            AnimatePhaseIn();
        }

        if (snapshot.Throws != _lastThrows)
        {
            _lastThrows = snapshot.Throws;
            AnimateIslandBulge();
        }

        var stalled = snapshot.StallBanner is not null;
        if (stalled)
        {
            FillStall(snapshot.StallMinutes);
        }
        else if (_sceneExpandActive)
        {
            FillSceneExpand();
        }

        MorphTo(stalled || _sceneExpandActive);
        Island.BorderBrush = stalled ? s_borderStall : s_borderIdle;
    }

    private void FillStall(int minutes)
    {

        ExpandedLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExpIcon.Visibility = Visibility.Visible;
        ExpText.Margin = new Thickness(12, 0, 0, 0);
        ExpSubtitle.Visibility = Visibility.Visible;
        ExpIcon.Text = "⚠️";
        ExpTitle.Text = "僵住";
        ExpTitle.Foreground = s_stallYellow;
        ExpSubtitle.Text = $"已 {minutes} 分钟无了结 · 仅通知，不停机";
    }

    private void FillSceneExpand()
    {

        ExpandedLayer.HorizontalAlignment = HorizontalAlignment.Center;
        ExpIcon.Visibility = Visibility.Collapsed;
        ExpText.Margin = new Thickness(0);
        ExpSubtitle.Visibility = Visibility.Collapsed;
        ExpTitle.Foreground = Brushes.White;
        if (_expandScene == "Battle")
        {
            ExpIcon.Text = "⚔️";
            ExpTitle.Text = _expandLeaving ? "退出战斗" : "战斗";
        }
        else if (_expandScene == "WorldMap")
        {
            ExpIcon.Text = "🗺️";
            ExpTitle.Text = _expandLeaving ? "退出地图" : "地图";
        }
        else
        {
            ExpIcon.Text = "🌍";
            ExpTitle.Text = _expandLeaving ? "退出大世界" : "大世界";
        }
    }

    private static bool IsKnownScene(string? scene) => scene is "Battle" or "OpenWorld" or "WorldMap";

    private void MorphTo(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;

        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty,
            new DoubleAnimation(expanded ? ExpandedWidth : CompactWidth, MorphDuration) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(expanded ? ExpandedHeight : CompactHeight, MorphDuration) { EasingFunction = ease });

        Island.CornerRadius = new CornerRadius(expanded ? ExpandedRadius : CompactRadius);

        var fade = TimeSpan.FromMilliseconds(160);
        var stagger = TimeSpan.FromMilliseconds(150);
        CompactLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 0 : 1, fade) { BeginTime = expanded ? TimeSpan.Zero : stagger });
        ExpandedLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 1 : 0, fade) { BeginTime = expanded ? stagger : TimeSpan.Zero });
    }

    private void SyncDotPulse(bool running)
    {
        if (running)
        {
            if (_pulse is not null) return;
            var anim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            };
            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, StateDot);
            Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            sb.Begin();
            _pulse = sb;
        }
        else
        {
            _pulse?.Stop();
            _pulse = null;
            StateDot.Opacity = 1;
        }
    }

    private void AnimatePhaseIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PhaseShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        PhaseText.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void AnimateIslandBulge()
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var settle = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };

        var sy = new DoubleAnimationUsingKeyFrames();
        sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))) { EasingFunction = easeOut });
        sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))) { EasingFunction = settle });
        IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);

        var sx = new DoubleAnimationUsingKeyFrames();
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))) { EasingFunction = easeOut });
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))) { EasingFunction = settle });
        IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
    }

    private static string SceneLabel(string? scene) => scene switch
    {
        "OpenWorld" => "大世界",
        "Battle" => "战斗",
        "WorldMap" => "地图",
        _ => "—",
    };

    private static SolidColorBrush SceneBrush(string? scene) => scene switch
    {
        "OpenWorld" => s_sceneWorld,
        "Battle" => s_sceneBattle,
        "WorldMap" => s_sceneMap,
        _ => s_sceneUnknown,
    };

    private static SolidColorBrush AccentFor(TaskState state) => state switch
    {
        TaskState.Running => s_accentRunning,
        TaskState.Paused => s_accentPaused,
        TaskState.Arming => s_accentArming,
        _ => s_accentNeutral,
    };

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
