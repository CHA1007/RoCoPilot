using System.Diagnostics;
using System.Runtime.InteropServices;
using RocoPilot.Input;

namespace RocoPilot.Scripting;

public sealed class HookStrokeRecorder : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmQuit = 0x0012;
    private const uint WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidEvInputSink = 0x100;
    private const uint RimTypeMouse = 0;
    private const uint LlMfInjected = 0x01;
    private const uint LlKfInjected = 0x10;
    private const uint LlKfUp = 0x80;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly HookProc _mouseHookProc;
    private readonly HookProc _keyHookProc;
    private readonly WndProc _windowProc;

    private Thread? _pumpThread;
    private int _pumpThreadId;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private IntPtr _window;
    private ManualResetEventSlim? _ready;
    private volatile bool _hookReady;

    private Stopwatch? _watch;
    private List<TimedStroke> _strokes = [];
    private int _rawAbsPrevX;
    private int _rawAbsPrevY;
    private bool _altDown;
    private Func<bool>? _isTargetFocused;
    private volatile bool _recording;

    public bool IsRecording => _recording;

    public HookStrokeRecorder()
    {
        _mouseHookProc = MouseHookProc;
        _keyHookProc = KeyHookProc;
        _windowProc = WindowProc;
    }

    public void Start(Func<bool>? isTargetFocused = null)
    {
        Thread thread;
        lock (_gate)
        {
            if (_recording) throw new InvalidOperationException("已在录制中。");

            _strokes = [];
            _rawAbsPrevX = 0;
            _rawAbsPrevY = 0;
            _isTargetFocused = isTargetFocused;
            _watch = Stopwatch.StartNew();
            _recording = true;
            _ready = new ManualResetEventSlim(false);
            thread = new Thread(PumpLoop) { IsBackground = true, Name = "hook-recorder-pump" };
            _pumpThread = thread;
            thread.Start();
        }

        if (!_ready.Wait(ReadyTimeout) || !_hookReady)
        {
            StopCore();
            lock (_gate) _recording = false;
            throw new InvalidOperationException("低级钩子或 Raw Input 安装失败/超时，录制未启动。");
        }
    }

    public RecordedScript Stop(string name)
    {
        lock (_gate)
        {
            if (!_recording) throw new InvalidOperationException("未在录制。");
            _recording = false;
        }

        StopCore();
        return new RecordedScript(name, _strokes);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
        }
        StopCore();
    }

    private void StopCore()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _pumpThread;
            _pumpThread = null;
        }
        if (thread is null) return;

        if (_pumpThreadId != 0) PostThreadMessage(_pumpThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        thread.Join(StopTimeout);

        lock (_gate)
        {
            _watch = null;
            _isTargetFocused = null;
        }
    }

    private void PumpLoop()
    {
        _pumpThreadId = GetCurrentThreadId();
        CreatePumpWindow();
        RegisterRawInput();
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, IntPtr.Zero, 0);
        _keyHook = SetWindowsHookEx(WhKeyboardLl, _keyHookProc, IntPtr.Zero, 0);
        _hookReady = _window != IntPtr.Zero && _mouseHook != IntPtr.Zero && _keyHook != IntPtr.Zero;
        _ready?.Set();
        if (!_hookReady) return;

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_mouseHook);
        UnhookWindowsHookEx(_keyHook);
        DestroyWindow(_window);
        _mouseHook = IntPtr.Zero;
        _keyHook = IntPtr.Zero;
        _window = IntPtr.Zero;
    }

    private void CreatePumpWindow()
    {
        var wc = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            Instance = GetModuleHandle(null),
            ClassName = "rocopilot-hook-recorder",
        };
        var atom = RegisterClassEx(ref wc);
        _window = atom == 0
            ? IntPtr.Zero
            : CreateWindowEx(0, new IntPtr(atom), string.Empty, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private void RegisterRawInput()
    {
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidEvInputSink, Target = _window },
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmInput && IsTargetFocused()) HandleRawInput(lParam);
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void HandleRawInput(IntPtr lParam)
    {
        var size = 0u;
        GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RawInputHeader>());
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, (uint)Marshal.SizeOf<RawInputHeader>()) != size)
                return;

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeMouse) return;

            var mouse = Marshal.PtrToStructure<RawMouse>(buffer + Marshal.SizeOf<RawInputHeader>());
            RecordRawMove(mouse.Flags, mouse.LastX, mouse.LastY);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void RecordRawMove(ushort flags, int lastX, int lastY)
    {
        lock (_gate)
        {
            var watch = _watch;
            if (watch is null) return;

            var delta = LowLevelHookMapper.RawMouseMove(flags, lastX, lastY, _rawAbsPrevX, _rawAbsPrevY);
            if ((flags & LowLevelHookMapper.RiMouseMoveAbsolute) != 0)
            {
                _rawAbsPrevX = lastX;
                _rawAbsPrevY = lastY;
            }

            if (delta.Dx == 0 && delta.Dy == 0) return;
            _strokes.Add(new TimedStroke(ReceivedDeviceKind.Mouse, 0, 0, 0, 0, delta.Dx, delta.Dy, watch.ElapsedMilliseconds));
        }
    }

    private IntPtr KeyHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

            if (info.VkCode == LowLevelHookMapper.VkMenu)
                _altDown = (info.Flags & LlKfUp) == 0;

            if (IsTargetFocused()
                && (info.Flags & LlKfInjected) == 0
                && !LowLevelHookMapper.IsSystemNavigationKey(info.VkCode, _altDown))
            {
                var (scanCode, state) = LowLevelHookMapper.MapKey((ushort)info.ScanCode, info.Flags);
                Record(ReceivedDeviceKind.Keyboard, scanCode, state, 0, 0, 0, 0);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsTargetFocused())
        {
            var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if ((info.Flags & LlMfInjected) == 0)
            {
                var evt = LowLevelHookMapper.MapMouse((ulong)wParam.ToInt64(), info.Pt.X, info.Pt.Y, info.MouseData);
                if (evt is { Kind: not HookMouseKind.Move } hookEvent) ApplyMouse(hookEvent);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void ApplyMouse(HookMouseEvent evt)
    {
        lock (_gate)
        {
            var watch = _watch;
            if (watch is null) return;

            if (evt.Kind == HookMouseKind.Button)
            {
                _strokes.Add(new TimedStroke(ReceivedDeviceKind.Mouse, 0, evt.State, 0, 0, 0, 0, watch.ElapsedMilliseconds));
            }
            else
            {
                _strokes.Add(new TimedStroke(ReceivedDeviceKind.Mouse, 0, evt.State, 0, evt.Rolling, 0, 0, watch.ElapsedMilliseconds));
            }
        }
    }

    private void Record(ReceivedDeviceKind kind, ushort code, ushort state, ushort flags, short rolling, int x, int y)
    {
        lock (_gate)
        {
            var watch = _watch;
            if (watch is null) return;
            _strokes.Add(new TimedStroke(kind, code, state, flags, rolling, x, y, watch.ElapsedMilliseconds));
        }
    }

    private bool IsTargetFocused()
    {
        var focused = _isTargetFocused;
        return focused is null || focused();
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern ushort RegisterClassEx(ref WndClassEx wc);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowEx(uint ex, IntPtr clsAtom, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint cmd, IntPtr buffer, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out NativeMsg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMsg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMsg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(int threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public NativePoint Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }
}