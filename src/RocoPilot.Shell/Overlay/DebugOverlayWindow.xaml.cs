using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Dispatch;

namespace RocoPilot.Shell.Overlay;
public partial class DebugOverlayWindow : Window
{
    private static readonly SolidColorBrush s_boxBrush = Frozen("#CC00E5FF");
    private static readonly SolidColorBrush s_activeBoxBrush = Frozen("#CCFF3D00");
    private static readonly SolidColorBrush s_labelBg = Frozen("#AA000000");
    private static readonly SolidColorBrush s_labelFg = Frozen("#FF00E5FF");
    private static readonly SolidColorBrush s_activeLabelFg = Frozen("#FFFF3D00");

    private readonly List<Rectangle> _boxes = new();
    private readonly List<TextBlock> _labels = new();
    private readonly TextBlock _scorePanel;

    public DebugOverlayWindow()
    {
        InitializeComponent();

        _scorePanel = new TextBlock
        {
            FontSize = 13,
            Background = s_labelBg,
            Foreground = Brushes.White,
            Padding = new Thickness(6, 3, 6, 3),
            Visibility = Visibility.Collapsed,
        };
        DebugCanvas.Children.Add(_scorePanel);
        Canvas.SetLeft(_scorePanel, 12);
        Canvas.SetTop(_scorePanel, 12);

        SourceInitialized += (_, _) => MakeClickThrough();
    }

    internal void Render(
        IReadOnlyList<StableTarget> targets,
        int frameWidth,
        int frameHeight,
        int activeTrackId,
        IReadOnlyDictionary<GameScene, float> sceneScores,
        GameScene currentScene)
    {
        RenderScorePanel(sceneScores, currentScene);

        if (frameWidth <= 0 || frameHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            CollapseFrom(0);
            return;
        }

        EnsurePool(targets.Count);
        var scaleX = ActualWidth / frameWidth;
        var scaleY = ActualHeight / frameHeight;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var isActive = target.TrackId == activeTrackId;
            var box = target.Latest;
            var x1 = box.X1 * scaleX;
            var y1 = box.Y1 * scaleY;
            var x2 = box.X2 * scaleX;
            var y2 = box.Y2 * scaleY;

            var rect = _boxes[i];
            rect.Width = Math.Max(1, x2 - x1);
            rect.Height = Math.Max(1, y2 - y1);
            rect.Stroke = isActive ? s_activeBoxBrush : s_boxBrush;
            rect.StrokeThickness = isActive ? 3 : 2;
            rect.Visibility = Visibility.Visible;
            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, y1);

            var text = _labels[i];
            text.Text = $"{PetNames.ToDisplay(box.ClassName)} {box.Confidence:F2}";
            text.Foreground = isActive ? s_activeLabelFg : s_labelFg;
            text.Visibility = Visibility.Visible;
            Canvas.SetLeft(text, x1);
            Canvas.SetTop(text, Math.Max(0, y1 - 20));
        }

        CollapseFrom(targets.Count);
    }

    private void RenderScorePanel(IReadOnlyDictionary<GameScene, float> sceneScores, GameScene currentScene)
    {
        if (sceneScores.Count == 0)
        {
            _scorePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var parts = new List<string>();
        foreach (var scene in new[] { GameScene.OpenWorld, GameScene.Battle, GameScene.WorldMap, GameScene.Unknown })
        {
            if (!sceneScores.TryGetValue(scene, out var score)) continue;
            var marker = scene == currentScene ? "● " : string.Empty;
            parts.Add($"{marker}{SceneLabel(scene)} {score:F2}");
        }

        _scorePanel.Text = "场景识别｜" + string.Join("  ·  ", parts);
        _scorePanel.Visibility = Visibility.Visible;
    }

    private static string SceneLabel(GameScene scene) => scene switch
    {
        GameScene.OpenWorld => "大世界",
        GameScene.Battle => "战斗",
        GameScene.WorldMap => "地图",
        _ => "未知",
    };

    private void EnsurePool(int count)
    {
        while (_boxes.Count < count)
        {
            var rect = new Rectangle { Visibility = Visibility.Collapsed };
            DebugCanvas.Children.Add(rect);
            _boxes.Add(rect);

            var text = new TextBlock
            {
                FontSize = 12,
                Background = s_labelBg,
                Padding = new Thickness(3, 1, 3, 1),
                Visibility = Visibility.Collapsed,
            };
            DebugCanvas.Children.Add(text);
            _labels.Add(text);
        }
    }

    private void CollapseFrom(int start)
    {
        for (var i = start; i < _boxes.Count; i++)
        {
            _boxes[i].Visibility = Visibility.Collapsed;
            _labels[i].Visibility = Visibility.Collapsed;
        }
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayNative.SetExStyle(
            hwnd,
            OverlayNative.GetExStyle(hwnd) | OverlayNative.WsExTransparent | OverlayNative.WsExToolWindow | OverlayNative.WsExNoActivate);
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromInvariantString(hex)!;
        brush.Freeze();
        return brush;
    }
}
