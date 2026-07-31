namespace RocoPilot.Capture;

public sealed record CaptureWindow(IntPtr Handle, string Title);

public static class WindowFinder
{
    public static IReadOnlyList<CaptureWindow> ListVisibleWindows()
    {
        var windows = new List<CaptureWindow>();
        unsafe
        {
            NativeMethods.EnumWindows(
                (hwnd, _) =>
                {
                    if (NativeMethods.IsWindowVisible(hwnd) && GetTitle(hwnd) is { Length: > 0 } title)
                    {
                        windows.Add(new CaptureWindow(hwnd, title));
                    }

                    return true;
                },
                IntPtr.Zero);
        }

        return windows;
    }

    public static IntPtr FindFirstByTitleSubstring(string? titleSubstring)
    {
        if (string.IsNullOrWhiteSpace(titleSubstring))
        {
            return IntPtr.Zero;
        }

        foreach (var window in ListVisibleWindows())
        {
            if (window.Title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                return window.Handle;
            }
        }

        return IntPtr.Zero;
    }

    public static unsafe string? GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var chars = new char[length + 1];
        fixed (char* p = chars)
        {
            var copied = NativeMethods.GetWindowTextW(hwnd, p, chars.Length);
            return copied <= 0 ? null : new string(p, 0, copied);
        }
    }

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
}
