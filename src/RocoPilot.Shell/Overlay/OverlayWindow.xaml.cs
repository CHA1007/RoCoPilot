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
    private const double SceneExpandWidth = 230, SceneExpandHeight = 46, SceneExpandRadius = 23;
    private const double StallWidth = 300, StallHeight = 72, StallRadius = 36;
    private const double MaxCompactWidth = 420;

    private static readonly TimeSpan SceneMorphDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan StallMorphDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan SceneExpandWindow = TimeSpan.FromMilliseconds(800);
    private const double ShineBandWidth = 80;
    private static readonly TimeSpan ShineDelay = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan ShineSweepDuration = TimeSpan.FromMilliseconds(450);

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
    private bool _stallActive;
    private string? _lastPhase;
    private string? _lastScene;
    private int _lastThrows;
    private double _compactWidth = CompactWidth;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
        Island.SizeChanged += (_, _) => UpdateShineClip();

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
            _lastScene = snapshot.Scene;
            SceneText.Text = SceneLabel(snapshot.Scene);
            SceneText.Foreground = SceneBrush(snapshot.Scene);

            if (IsKnownScene(snapshot.Scene))
            {
                _sceneExpandActive = true;
                _shrinkTimer.Stop();
                _shrinkTimer.Start();
                AnimateSceneIn();
                PlaySceneEffects();
            }
        }

        var phase = snapshot.Phase;
        if (!string.Equals(phase, _lastPhase, StringComparison.Ordinal))
        {
            _lastPhase = phase;
            PhaseText.Text = phase ?? "待机";
            AnimatePhaseIn();
        }

        UpdateCompactWidth();

        if (snapshot.Throws != _lastThrows)
        {
            _lastThrows = snapshot.Throws;
            AnimateIslandBulge();
        }

        var stalled = snapshot.StallBanner is not null;
        _stallActive = stalled;
        if (stalled)
        {
            FillStall(snapshot.StallMinutes);
        }

        MorphTo(stalled || _sceneExpandActive);
        Island.BorderBrush = stalled ? s_borderStall : s_borderIdle;
    }

    private void UpdateCompactWidth()
    {
        PhaseText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SceneText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var needed = 14 + 8 + 9 + PhaseText.DesiredSize.Width + 12 + SceneText.DesiredSize.Width + 14;
        var target = Math.Clamp(needed, CompactWidth, MaxCompactWidth);
        if (Math.Abs(target - _compactWidth) < 0.5) return;

        _compactWidth = target;
        var windowWidth = target + 48;
        if (Width < windowWidth) Width = windowWidth;

        if (!_expanded)
        {
            Island.BeginAnimation(WidthProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }
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

    private void MorphTo(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;

        double targetWidth, targetHeight, targetRadius;
        TimeSpan duration;
        if (_stallActive)
        {
            targetWidth = StallWidth;
            targetHeight = StallHeight;
            targetRadius = StallRadius;
            duration = StallMorphDuration;
        }
        else if (_sceneExpandActive)
        {
            targetWidth = Math.Max(SceneExpandWidth, _compactWidth + 48);
            targetHeight = SceneExpandHeight;
            targetRadius = SceneExpandRadius;
            duration = SceneMorphDuration;
        }
        else
        {
            targetWidth = _compactWidth;
            targetHeight = CompactHeight;
            targetRadius = CompactRadius;
            duration = SceneMorphDuration;
        }

        var ease = _stallActive
            ? (EasingFunctionBase)new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty,
            new DoubleAnimation(targetWidth, duration) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(targetHeight, duration) { EasingFunction = ease });

        Island.CornerRadius = new CornerRadius(targetRadius);

        var fade = TimeSpan.FromMilliseconds(160);
        var stagger = TimeSpan.FromMilliseconds(150);
        if (_stallActive)
        {
            CompactLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fade));
            ExpandedLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, fade) { BeginTime = stagger });
        }
        else
        {
            CompactLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, fade));
            ExpandedLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fade));
        }
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

    private void AnimateSceneIn()
    {
        SceneShift.BeginAnimation(TranslateTransform.YProperty, null);
        SceneScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SceneScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        SceneText.BeginAnimation(OpacityProperty, null);

        SceneShift.Y = -40;
        SceneScale.ScaleX = 1.15;
        SceneScale.ScaleY = 1.15;
        SceneText.Opacity = 0;

        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var delay = TimeSpan.FromMilliseconds(150);

        var shiftY = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        shiftY.KeyFrames.Add(new EasingDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        shiftY.KeyFrames.Add(new EasingDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))) { EasingFunction = easeIn });
        shiftY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))) { EasingFunction = easeOut });
        SceneShift.BeginAnimation(TranslateTransform.YProperty, shiftY);

        var scaleX = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))) { EasingFunction = easeOut });
        var scaleY = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))) { EasingFunction = easeOut });
        SceneScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        SceneScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

        SceneText.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = delay,
                EasingFunction = easeOut,
            });
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

    private void PlaySceneEffects()
    {
        PlayShineSweep();
    }

    private void UpdateShineClip()
    {
        var radius = Island.CornerRadius.TopLeft;
        ShineClip.Rect = new Rect(0, 0, Island.ActualWidth, Island.ActualHeight);
        ShineClip.RadiusX = radius;
        ShineClip.RadiusY = radius;
    }

    private void PlayShineSweep()
    {
        var islandWidth = Math.Max(Island.ActualWidth, CompactWidth);
        var easeTravel = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var travel = TimeSpan.FromMilliseconds(ShineDelay.TotalMilliseconds + ShineSweepDuration.TotalMilliseconds);
        var shiftX = new DoubleAnimationUsingKeyFrames();
        shiftX.KeyFrames.Add(new LinearDoubleKeyFrame(-ShineBandWidth, KeyTime.FromTimeSpan(ShineDelay)));
        shiftX.KeyFrames.Add(new EasingDoubleKeyFrame(islandWidth, KeyTime.FromTimeSpan(travel)) { EasingFunction = easeTravel });
        ShineShift.BeginAnimation(TranslateTransform.XProperty, shiftX);

        var fadeIn = TimeSpan.FromMilliseconds(110);
        var fadeOut = TimeSpan.FromMilliseconds(140);
        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(ShineDelay)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(ShineDelay + fadeIn)) { EasingFunction = easeOut });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(ShineDelay + ShineSweepDuration - fadeOut)) { EasingFunction = easeIn });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(ShineDelay + ShineSweepDuration)) { EasingFunction = easeIn });
        ShineSweep.BeginAnimation(OpacityProperty, opacity);
    }

    private static bool IsKnownScene(string? scene) =>
        scene is "OpenWorld" or "Battle" or "WorldMap";

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
