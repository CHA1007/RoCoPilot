using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RocoPilot.Shell.Hotkeys;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WhKeyboardLl = 13;

    private const int WmKeyDown = 0x0100;

    private const int WmKeyUp = 0x0101;

    private const int WmSysKeyDown = 0x0104;

    private const int WmSysKeyUp = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VkCode;

        public uint ScanCode;

        public uint Flags;

        public uint Time;

        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly object _gate = new();
    private readonly Dictionary<string, (HotkeyBinding Binding, Action Callback)> _bindings = new();
    private readonly HashSet<Key> _heldKeys = [];
    private readonly LowLevelKeyboardProc _proc;
    private SynchronizationContext? _syncContext;
    private IntPtr _hook = IntPtr.Zero;

    public GlobalHotkeyManager() => _proc = HookProc;

    public bool IsRunning
    {
        get { lock (_gate) { return _hook != IntPtr.Zero; } }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero) return;
            _syncContext = SynchronizationContext.Current;
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Trace.TraceWarning($"[GlobalHotkeyManager] SetWindowsHookEx failed win32Error={Marshal.GetLastWin32Error()}");
            }
            else
            {
                Trace.TraceInformation("[GlobalHotkeyManager] keyboard hook installed");
            }
        }
    }

    public bool Register(string owner, string hotkey, Action callback)
    {
        lock (_gate)
        {
            _bindings.Remove(owner);
            if (!HotkeyBinding.TryParse(hotkey, out var binding)) return false;
            _bindings[owner] = (binding, callback);
            Trace.TraceInformation($"[GlobalHotkeyManager] registered owner={owner} hotkey={hotkey}");
            return true;
        }
    }

    public void Unregister(string owner)
    {
        lock (_gate)
        {
            _bindings.Remove(owner);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _bindings.Clear();
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var key = KeyInterop.KeyFromVirtualKey((int)Marshal.PtrToStructure<KbdllHookStruct>(lParam).VkCode);

            switch (message)
            {
                case WmKeyUp:
                case WmSysKeyUp:
                    lock (_gate) { _heldKeys.Remove(key); }
                    break;

                case WmKeyDown:
                case WmSysKeyDown:
                    if (ForegroundIsOwnProcess())
                    {
                        break;
                    }

                    var callback = TakeMatchingCallback(key);
                    if (callback is not null)
                    {
                        DispatchCallback(callback);
                        return (IntPtr)1;
                    }

                    break;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void DispatchCallback(Action callback)
    {
        void SafeInvoke()
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[GlobalHotkeyManager] hotkey callback failed: {ex}");
            }
        }

        var syncContext = _syncContext;
        if (syncContext is null)
        {
            SafeInvoke();
        }
        else
        {
            syncContext.Post(_ => SafeInvoke(), null);
        }
    }

    private Action? TakeMatchingCallback(Key key)
    {
        if (IsModifier(key)) return null;

        lock (_gate)
        {
            if (!_heldKeys.Add(key)) return null;

            foreach (var (binding, callback) in _bindings.Values)
            {
                if (binding.Key == key && binding.Modifiers == CurrentModifiers())
                {
                    return callback;
                }
            }

            return null;
        }
    }

    private static bool ForegroundIsOwnProcess()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static ModifierKeys CurrentModifiers()
    {
        var mods = ModifierKeys.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= ModifierKeys.Control;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= ModifierKeys.Alt;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= ModifierKeys.Shift;
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)
        {
            mods |= ModifierKeys.Windows;
        }

        return mods;
    }
}
