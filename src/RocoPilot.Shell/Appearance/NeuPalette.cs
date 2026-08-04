using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace RocoPilot.Shell.Appearance;

internal static class NeuPalette
{
    private static bool _installed;

    public static void Install()
    {
        if (!_installed)
        {
            _installed = true;
            ApplicationThemeManager.Changed += (theme, _) => Apply(theme);
        }

        Apply(ApplicationThemeManager.GetAppTheme());
    }

    private static void Apply(ApplicationTheme theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        if (theme == ApplicationTheme.Light)
        {
            resources["NeuLightShadowColor"] = Color.FromRgb(0xFF, 0xFF, 0xFF);
            resources["NeuDarkShadowColor"] = Color.FromRgb(0xA8, 0xB6, 0xCC);
            resources["NeuBase"] = Brush(0xE4, 0xE9, 0xF2);
            resources["NeuSunkenFill"] = Brush(0xDC, 0xE2, 0xEC);
            resources["NeuTextPrimary"] = Brush(0x35, 0x41, 0x5A);
            resources["NeuTextSecondary"] = Brush(0x66, 0x71, 0x8A);
            resources["NeuAccent"] = Brush(0x2E, 0x6B, 0xE6);
            resources["NeuOnAccent"] = Brush(0xFF, 0xFF, 0xFF);
            resources["NeuDanger"] = Brush(0xC4, 0x2B, 0x1C);
        }
        else
        {
            resources["NeuLightShadowColor"] = Color.FromRgb(0x34, 0x38, 0x3F);
            resources["NeuDarkShadowColor"] = Color.FromRgb(0x16, 0x18, 0x1C);
            resources["NeuBase"] = Brush(0x28, 0x2B, 0x31);
            resources["NeuSunkenFill"] = Brush(0x22, 0x25, 0x2A);
            resources["NeuTextPrimary"] = Brush(0xE9, 0xEB, 0xF1);
            resources["NeuTextSecondary"] = Brush(0x9A, 0xA2, 0xB1);
            resources["NeuAccent"] = Brush(0x5B, 0x8D, 0xEF);
            resources["NeuOnAccent"] = Brush(0xFF, 0xFF, 0xFF);
            resources["NeuDanger"] = Brush(0xFF, 0x7A, 0x6B);
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
