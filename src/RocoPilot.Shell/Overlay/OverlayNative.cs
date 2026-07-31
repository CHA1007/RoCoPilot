using System.Runtime.InteropServices;

namespace RocoPilot.Shell.Overlay;

internal static partial class OverlayNative
{
    public const int GwlExStyle = -20;

    public const int WsExTransparent = 0x00000020;

    public const int WsExToolWindow = 0x00000080;

    public const int WsExNoActivate = 0x08000000;

    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SwpNoSize = 0x0001;

    public const uint SwpNoMove = 0x0002;

    public const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLong64(hWnd, GwlExStyle) : (IntPtr)GetWindowLong32(hWnd, GwlExStyle);

    public static void SetExStyle(IntPtr hWnd, IntPtr style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLong64(hWnd, GwlExStyle, style);
        }
        else
        {
            SetWindowLong32(hWnd, GwlExStyle, (int)style);
        }
    }
}
