using System.Windows.Media;

namespace RocoPilot.Shell.Appearance;

internal static class RouteVisuals
{
    // 现代工具风：干净简约，中性色为主，蓝作唯一强调，绿/红为语义色
    internal static readonly Color Accent = Color.FromRgb(0x4F, 0x6D, 0xF5);
    internal static readonly SolidColorBrush AccentBrush = Frozen(Accent);
    internal static readonly SolidColorBrush AccentHoverBrush = Frozen(Color.FromRgb(0x5C, 0x79, 0xF7));
    internal static readonly SolidColorBrush AccentPressedBrush = Frozen(Color.FromRgb(0x40, 0x5C, 0xE0));
    internal static readonly SolidColorBrush AccentSoftBrush = Frozen(Color.FromArgb(0x14, 0x4F, 0x6D, 0xF5));

    internal static readonly Color Start = Color.FromRgb(0x2F, 0xB3, 0x64);
    internal static readonly SolidColorBrush StartBrush = Frozen(Start);
    internal static readonly SolidColorBrush StartSoftBrush = Frozen(Color.FromArgb(0x1A, 0x2F, 0xB3, 0x64));

    internal static readonly Color End = Color.FromRgb(0xE5, 0x48, 0x4D);
    internal static readonly SolidColorBrush EndBrush = Frozen(End);
    internal static readonly SolidColorBrush EndSoftBrush = Frozen(Color.FromArgb(0x1A, 0xE5, 0x48, 0x4D));

    internal static readonly SolidColorBrush InkBrush = Frozen(Color.FromRgb(0x1F, 0x23, 0x29));
    internal static readonly SolidColorBrush InkSecondaryBrush = Frozen(Color.FromRgb(0x4D, 0x55, 0x63));
    internal static readonly SolidColorBrush InkTertiaryBrush = Frozen(Color.FromRgb(0x8A, 0x93, 0xA2));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}