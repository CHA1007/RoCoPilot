using System.Windows.Input;

namespace RocoPilot.Shell.Hotkeys;

public readonly record struct HotkeyBinding(Key Key, ModifierKeys Modifiers)
{
    public const uint ModAlt = 0x0001;

    public const uint ModControl = 0x0002;

    public const uint ModShift = 0x0004;

    public const uint ModWin = 0x0008;

    public const uint ModNoRepeat = 0x4000;

    public uint Win32Modifiers
    {
        get
        {
            uint mods = ModNoRepeat;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModAlt;
            if (Modifiers.HasFlag(ModifierKeys.Control)) mods |= ModControl;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModShift;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModWin;
            return mods;
        }
    }

    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = Key.None;
        var mods = ModifierKeys.None;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; continue;
                case "alt": mods |= ModifierKeys.Alt; continue;
                case "shift": mods |= ModifierKeys.Shift; continue;
                case "win": mods |= ModifierKeys.Windows; continue;
            }

            if (!Enum.TryParse<Key>(part, ignoreCase: true, out var parsed) || parsed == Key.None)
            {
                return false;
            }

            key = parsed;
        }

        if (key == Key.None) return false;
        binding = new HotkeyBinding(key, mods);
        return true;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
