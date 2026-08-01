using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RocoPilot.Core;

namespace RocoPilot.Shell.Overlay;

public partial class OverlayWindow : Window
{
    private static readonly SolidColorBrush s_brushRunning = Frozen("#E816A34A");
    private static readonly SolidColorBrush s_brushPaused = Frozen("#E8DC2626");
    private static readonly SolidColorBrush s_brushArming = Frozen("#E82563EB");
    private static readonly SolidColorBrush s_brushStopping = Frozen("#E864748B");
    private static readonly SolidColorBrush s_brushIdle = Frozen("#E8334155");

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
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
        var stamp = Describe(snapshot.State);
        StampText.Text = stamp;
        StateValue.Text = stamp;
        CardBorder.Background = BrushFor(snapshot.State);

        PhaseValue.Text = snapshot.Phase ?? "—";
        ThrowValue.Text = snapshot.Throws.ToString(CultureInfo.InvariantCulture);

        StallText.Text = snapshot.StallBanner;
        StallBanner.Visibility = snapshot.StallBanner is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Describe(TaskState state) => state switch
    {
        TaskState.Idle => "空闲",
        TaskState.Arming => "启动中",
        TaskState.Running => "运行中",
        TaskState.Paused => "已暂停",
        TaskState.Stopping => "收尾中",
        _ => state.ToString(),
    };

    private static SolidColorBrush BrushFor(TaskState state) => state switch
    {
        TaskState.Running => s_brushRunning,
        TaskState.Paused => s_brushPaused,
        TaskState.Arming => s_brushArming,
        TaskState.Stopping => s_brushStopping,
        _ => s_brushIdle,
    };

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
