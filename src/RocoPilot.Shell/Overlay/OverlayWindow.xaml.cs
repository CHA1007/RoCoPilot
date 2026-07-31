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
        var stamp = Describe(snapshot.State, snapshot.CaptureRunning);
        StampText.Text = stamp;
        StateValue.Text = stamp;
        CardBorder.Background = BrushFor(snapshot.State, snapshot.CaptureRunning);

        PhaseValue.Text = snapshot.Phase ?? "—";
        SettleValue.Text = FormatSinceSettle(snapshot.SinceLastSettle);
        ThrowValue.Text = snapshot.Throws.ToString(CultureInfo.InvariantCulture);

        ArmingText.Text = snapshot.ArmingLine;
        ArmingText.Visibility = snapshot.ArmingLine is null ? Visibility.Collapsed : Visibility.Visible;
        FailureText.Text = snapshot.FailureLine;
        FailureText.Visibility = snapshot.FailureLine is null ? Visibility.Collapsed : Visibility.Visible;
        StallText.Text = snapshot.StallBanner;
        StallBanner.Visibility = snapshot.StallBanner is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Describe(TaskState state, bool captureRunning) => state switch
    {
        TaskState.Idle when captureRunning => "截图中",
        TaskState.Idle => "空闲",
        TaskState.Arming => "自检中",
        TaskState.Running => "运行中",
        TaskState.Paused => "已暂停",
        TaskState.Stopping => "收尾中",
        _ => state.ToString(),
    };

    private static SolidColorBrush BrushFor(TaskState state, bool captureRunning) => state switch
    {
        TaskState.Idle when captureRunning => s_brushArming,
        TaskState.Running => s_brushRunning,
        TaskState.Paused => s_brushPaused,
        TaskState.Arming => s_brushArming,
        TaskState.Stopping => s_brushStopping,
        _ => s_brushIdle,
    };

    private static string FormatSinceSettle(TimeSpan? sinceSettle)
    {
        if (sinceSettle is not { } t)
        {
            return "—";
        }

        return t.TotalHours >= 1
            ? ((int)t.TotalHours).ToString(CultureInfo.InvariantCulture) + t.ToString(@"\:mm\:ss")
            : t.ToString(@"mm\:ss");
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
