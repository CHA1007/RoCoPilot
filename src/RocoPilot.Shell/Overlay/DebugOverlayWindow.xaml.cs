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
    private static readonly SolidColorBrush s_labelBg = Frozen("#AA000000");
    private static readonly SolidColorBrush s_labelFg = Frozen("#FF00E5FF");

    public DebugOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    /// <summary>重绘检测框。坐标从捕获帧空间映射到窗口空间。</summary>
    internal void Render(IReadOnlyList<StableTarget> targets, int frameWidth, int frameHeight)
    {
        DebugCanvas.Children.Clear();
        if (frameWidth <= 0 || frameHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scaleX = ActualWidth / frameWidth;
        var scaleY = ActualHeight / frameHeight;

        foreach (var target in targets)
        {
            var box = target.Latest;
            var x1 = box.X1 * scaleX;
            var y1 = box.Y1 * scaleY;
            var x2 = box.X2 * scaleX;
            var y2 = box.Y2 * scaleY;

            var rect = new Rectangle
            {
                Width = Math.Max(1, x2 - x1),
                Height = Math.Max(1, y2 - y1),
                Stroke = s_boxBrush,
                StrokeThickness = 2,
            };
            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, y1);
            DebugCanvas.Children.Add(rect);

            var label = $"{box.ClassName} {box.Confidence:F2}";
            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = s_labelFg,
                Background = s_labelBg,
                Padding = new Thickness(3, 1, 3, 1),
            };
            Canvas.SetLeft(text, x1);
            Canvas.SetTop(text, Math.Max(0, y1 - 20));
            DebugCanvas.Children.Add(text);
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
