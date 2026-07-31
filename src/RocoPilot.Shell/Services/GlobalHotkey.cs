using System.Runtime.InteropServices;

namespace RocoPilot.Shell.Services;

internal static partial class GlobalHotkey
{
    public const int WmHotKey = 0x0312;

    public const uint VkF12 = 0x7B;

    public const int IdPauseToggle = 1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
