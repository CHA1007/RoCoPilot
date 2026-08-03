using System.Diagnostics;

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

    public static IntPtr FindByProcessName(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    return proc.MainWindowHandle;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }
    }

    public static bool IsForegroundProcess(string processName)
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var proc in processes)
            {
                if (proc.Id == fgPid) return true;
            }

            return false;
        }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }
    }

    public static bool ActivateWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.SetForegroundWindow(hwnd);

    public const string GameProcessName = "NRC-Win64-Shipping";

    public static void ActivateGameWindow()
    {
        var hwnd = FindByProcessName(GameProcessName);
        if (hwnd != IntPtr.Zero)
        {
            ActivateWindow(hwnd);
        }
    }
}
