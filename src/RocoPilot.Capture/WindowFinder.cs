using System.Diagnostics;

namespace RocoPilot.Capture;

public sealed record CaptureWindow(IntPtr Handle, string Title);

public static class WindowFinder
{
    public const string GameProcessName = "NRC-Win64-Shipping";

    public static IReadOnlyList<CaptureWindow> ListAppWindows()
    {
        var windows = new List<CaptureWindow>();
        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                if (IsAppWindow(hwnd) && GetTitle(hwnd) is { Length: > 0 } title)
                {
                    windows.Add(new CaptureWindow(hwnd, title));
                }

                return true;
            },
            IntPtr.Zero);
        return windows;
    }

    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, 4) == 0 && cloaked != 0)
        {
            return false;
        }

        const long ignoredExStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & ignoredExStyle) != 0)
        {
            return false;
        }

        return NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero;
    }

    public static IntPtr FindFirstByTitleSubstring(string? titleSubstring)
    {
        if (string.IsNullOrWhiteSpace(titleSubstring))
        {
            return IntPtr.Zero;
        }

        var hwnd = FindMatchingWindow(titleSubstring, visibleOnly: true);
        if (hwnd != IntPtr.Zero)
        {
            return hwnd;
        }

        return FindMatchingWindow(titleSubstring, visibleOnly: false);
    }

    private static IntPtr FindMatchingWindow(string titleSubstring, bool visibleOnly)
    {
        IntPtr match = IntPtr.Zero;
        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                if (visibleOnly && !NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                if (GetTitle(hwnd) is { Length: > 0 } title
                    && title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    match = hwnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return match;
    }

    public static string? GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var chars = new char[length + 1];
        unsafe
        {
            fixed (char* p = chars)
            {
                var copied = NativeMethods.GetWindowTextW(hwnd, p, chars.Length);
                return copied <= 0 ? null : new string(p, 0, copied);
            }
        }
    }

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }

    public static IntPtr FindByProcessName(string processName)
    {
        var foreground = FindForegroundByProcessName(processName);
        if (foreground != IntPtr.Zero)
        {
            return foreground;
        }

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
        }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }

        return FindMainWindowByProcessName(processName);
    }

    public static IntPtr FindForegroundByProcessName(string processName)
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
        return IsProcessIdNamed(fgPid, processName) ? fg : IntPtr.Zero;
    }

    public static bool IsForegroundProcess(string processName)
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
        return IsProcessIdNamed(fgPid, processName);
    }

    public static bool IsForegroundProcess(int processId)
    {
        if (processId <= 0) return false;
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
        return fgPid == processId;
    }

    public static int FindProcessId(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var proc in processes)
            {
                return proc.Id;
            }
        }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }

        return 0;
    }

    public static bool ActivateWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.SetForegroundWindow(hwnd);

    public static void ActivateGameWindow()
    {
        var hwnd = FindByProcessName(GameProcessName);
        if (hwnd != IntPtr.Zero)
        {
            ActivateWindow(hwnd);
        }
    }

    private static IntPtr FindMainWindowByProcessName(string processName)
    {
        var pids = new List<int>();
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var proc in processes)
            {
                pids.Add(proc.Id);
            }
        }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }

        if (pids.Count == 0)
        {
            return IntPtr.Zero;
        }

        IntPtr match = IntPtr.Zero;
        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pids.Contains(pid) && NativeMethods.IsWindowVisible(hwnd))
                {
                    match = hwnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return match;
    }

    private static bool IsProcessIdNamed(int processId, string processName)
    {
        if (processId <= 0)
        {
            return false;
        }

        using var proc = Process.GetProcessById(processId);
        return string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
    }
}