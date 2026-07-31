using System.Text;

namespace RocoPilot.Input;

public static class KeyNames
{
    private static readonly Dictionary<string, InputKey> Table = Build();

    private static Dictionary<string, InputKey> Build()
    {
        var t = new Dictionary<string, InputKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["lmb"] = InputKey.LeftMouse,
            ["rmb"] = InputKey.RightMouse,
            ["mmb"] = InputKey.MiddleMouse,
            ["backspace"] = InputKey.Keyboard(0x08),
            ["tab"] = InputKey.Keyboard(0x09),
            ["enter"] = InputKey.Keyboard(0x0D),
            ["shift"] = InputKey.Keyboard(0x10),
            ["ctrl"] = InputKey.Keyboard(0x11),
            ["alt"] = InputKey.Keyboard(0x12),
            ["esc"] = InputKey.Keyboard(0x1B),
            ["space"] = InputKey.Keyboard(0x20),
            ["left"] = InputKey.Keyboard(0x25),
            ["up"] = InputKey.Keyboard(0x26),
            ["right"] = InputKey.Keyboard(0x27),
            ["down"] = InputKey.Keyboard(0x28),
        };
        for (var i = 0; i < 12; i++)
        {
            t[$"f{i + 1}"] = InputKey.Keyboard((ushort)(0x70 + i));
        }
        for (var c = 'a'; c <= 'z'; c++)
        {
            t[c.ToString()] = InputKey.Keyboard((ushort)char.ToUpperInvariant(c));
        }
        for (var c = '0'; c <= '9'; c++)
        {
            t[c.ToString()] = InputKey.Keyboard((ushort)c);
        }
        return t;
    }

    public static IReadOnlyCollection<string> Supported => Table.Keys;

    public static InputKey Parse(string name)
    {
        var key = name?.Trim() ?? string.Empty;
        if (Table.TryGetValue(key, out var parsed))
        {
            return parsed;
        }

        var list = new StringBuilder();
        foreach (var k in Table.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (list.Length > 0) list.Append(", ");
            list.Append(k);
        }
        throw new ArgumentException($"未知键位 \"{name}\"；支持: {list}", nameof(name));
    }
}
