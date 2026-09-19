using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RocoPilot.Settings;

namespace RocoPilot.Shell.Services;

public sealed class MouseButtonMonitor : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint LlMfInjected = 0x01;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly HookProc _hookProc;
    private readonly ConcurrentQueue<ButtonEvent> _pending = new();
    private readonly string _logPath;

    private Thread? _pumpThread;
    private Thread? _flushThread;
    private int _pumpThreadId;
    private IntPtr _hook;
    private Stopwatch? _watch;
    private long _lastEventMs;
    private ManualResetEventSlim? _readySignal;
    private volatile bool _running;

    public MouseButtonMonitor() : this(Path.Combine(RocoPaths.LogsRoot, "mouse-watch.jsonl")) { }

    internal MouseButtonMonitor(string logPath)
    {
        _hookProc = MouseHookProc;
        _logPath = logPath;
    }

    private sealed record ButtonEvent(string Button, bool IsDown, long ElapsedMs, long DeltaMs, bool Injected);

    public bool IsRunning => _running;

    public void Start()
    {
        Thread thread;
        var ready = new ManualResetEventSlim(false);
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("已在监听中。");

            _watch = Stopwatch.StartNew();
            _lastEventMs = 0;
            _running = true;
            _readySignal = ready;
            thread = new Thread(PumpLoop) { IsBackground = true, Name = "mouse-button-monitor" };
            _pumpThread = thread;
            thread.Start();
            _flushThread = new Thread(FlushLoop) { IsBackground = true, Name = "mouse-button-monitor-flush" };
            _flushThread.Start();
        }

        if (!ready.Wait(ReadyTimeout) || _hook == IntPtr.Zero)
        {
            Stop();
            throw new InvalidOperationException("低级鼠标钩子安装失败/超时，监听未启动。");
        }
    }

    public void Stop()
    {
        Thread? pump;
        Thread? flush;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            pump = _pumpThread;
            flush = _flushThread;
            _pumpThread = null;
            _flushThread = null;
        }

        if (pump is not null)
        {
            if (_pumpThreadId != 0) PostThreadMessage(_pumpThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            pump.Join(StopTimeout);
        }

        flush?.Join(StopTimeout);
        FlushOnce();

        lock (_gate)
        {
            _watch = null;
            _readySignal = null;
        }
    }

    public void Dispose() => Stop();

    private void PumpLoop()
    {
        _pumpThreadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WhMouseLl, _hookProc, IntPtr.Zero, 0);
        lock (_gate) _readySignal?.Set();
        if (_hook == IntPtr.Zero) return;

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private void FlushLoop()
    {
        while (_running)
        {
            FlushOnce();
            Thread.Sleep(FlushInterval);
        }
    }

    private void FlushOnce()
    {
        var builder = new StringBuilder();
        while (_pending.TryDequeue(out var evt))
        {
            builder.AppendLine(
                $"{{\"t\":{DateTimeOffset.Now.ToUnixTimeMilliseconds()},\"button\":\"{evt.Button}\"," +
                $"\"action\":\"{(evt.IsDown ? "down" : "up")}\",\"elapsed_ms\":{evt.ElapsedMs}," +
                $"\"delta_ms\":{evt.DeltaMs},\"injected\":{(evt.Injected ? "true" : "false")}}}");
        }

        if (builder.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, builder.ToString());
        }
        catch (Exception)
        {
        }
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var button = ButtonName(wParam.ToInt64());
            if (button is not null)
            {
                var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                var watch = _watch;
                if (watch is not null)
                {
                    var elapsed = watch.ElapsedMilliseconds;
                    var delta = elapsed - _lastEventMs;
                    _lastEventMs = elapsed;
                    var isDown = IsDownMessage(wParam.ToInt64());
                    _pending.Enqueue(new ButtonEvent(button, isDown, elapsed, delta, (info.Flags & LlMfInjected) != 0));
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static string? ButtonName(long message) => message switch
    {
        WmLeftButtonDown or WmLeftButtonUp => "左键",
        WmRightButtonDown or WmRightButtonUp => "右键",
        WmMiddleButtonDown or WmMiddleButtonUp => "中键",
        WmXButtonDown or WmXButtonUp => "侧键",
        _ => null,
    };

    private static bool IsDownMessage(long message) => message switch
    {
        WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown => true,
        _ => false,
    };

    private const long WmLeftButtonDown = 0x0201;
    private const long WmLeftButtonUp = 0x0202;
    private const long WmRightButtonDown = 0x0204;
    private const long WmRightButtonUp = 0x0205;
    private const long WmMiddleButtonDown = 0x0207;
    private const long WmMiddleButtonUp = 0x0208;
    private const long WmXButtonDown = 0x020B;
    private const long WmXButtonUp = 0x020C;
    private const uint WmQuit = 0x0012;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

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
    private struct NativeMsg
    {
        public IntPtr Hwnd;
        public IntPtr Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Pt;
    }
}
