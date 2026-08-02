using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RocoPilot.Detection;

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

    public DebugOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    /// <summary>重绘检测框。坐标从捕获帧空间映射到窗口空间。</summary>
    internal void Render(IReadOnlyList<StableTarget> targets, int frameWidth, int frameHeight, int activeTrackId = -1)
    {
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
