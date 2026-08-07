using System.Windows.Media;

namespace RocoPilot.Shell.Appearance;

internal static class RouteVisuals
{
    internal static readonly Color Accent = Color.FromRgb(0xE0, 0x8A, 0x00);
    internal static readonly SolidColorBrush AccentBrush = Frozen(Accent);
    internal static readonly SolidColorBrush AccentHoverBrush = Frozen(Color.FromRgb(0xF0, 0x9A, 0x10));
    internal static readonly SolidColorBrush AccentPressedBrush = Frozen(Color.FromRgb(0xC0, 0x74, 0x00));
    internal static readonly SolidColorBrush AccentGhostHoverBrush = Frozen(Color.FromArgb(0x26, 0xE0, 0x8A, 0x00));
    internal static readonly SolidColorBrush AccentGhostPressedBrush = Frozen(Color.FromArgb(0x40, 0xE0, 0x8A, 0x00));
    internal static readonly SolidColorBrush AccentSoftBrush = Frozen(Color.FromArgb(0x30, 0xE0, 0x8A, 0x00));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}