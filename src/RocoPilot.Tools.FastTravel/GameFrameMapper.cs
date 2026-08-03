using System.Runtime.InteropServices;
using RocoPilot.Capture;

namespace RocoPilot.Tools.FastTravel;

public static class GameFrameMapper
{
    public static Func<int, int, (int X, int Y)> Create(ICaptureSource source)
    {
        if (source.BackendName != CaptureBackends.WgcWindow)
            return (x, y) => (x, y);

        var frameW = Math.Max(1, source.FrameWidth);
        var frameH = Math.Max(1, source.FrameHeight);

        return (x, y) =>
        {
            var hwnd = WindowFinder.FindByProcessName(WindowFinder.GameProcessName);
            if (hwnd == IntPtr.Zero)
                return (x, y);

            var origin = new Point { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref origin))
                return (x, y);

            if (!GetClientRect(hwnd, out var client))
                return (origin.X + x, origin.Y + y);

            var sx = (double)client.Right / frameW;
            var sy = (double)client.Bottom / frameH;
            return (origin.X + (int)(x * sx), origin.Y + (int)(y * sy));
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);
}
