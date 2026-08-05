using System.Collections.Concurrent;
using RocoPilot.Input.Native;

namespace RocoPilot.Input.Interception;

public sealed class InterceptionDriver : IInputDriver
{
    private sealed record DeviceClass(int Lo, int Hi, string Kind, string Verb, bool IsMouse)
    {
        public static readonly DeviceClass Mouse = new(
            InterceptionConstants.MouseDeviceMin, InterceptionConstants.MouseDeviceMax,
            "鼠标", "动一下鼠标", IsMouse: true);

        public static readonly DeviceClass Keyboard = new(
            InterceptionConstants.KeyboardDeviceMin, InterceptionConstants.KeyboardDeviceMax,
            "键盘", "随便按一个键", IsMouse: false);
    }

    private const uint RelayWaitMs = 15;

    private readonly IInterceptionApi _api;
    private readonly Func<ushort, ushort> _scanCodeMapper;
    private readonly object _relayGate = new();
    private readonly ConcurrentQueue<Action> _relaySends = new();
    private IntPtr _context = IntPtr.Zero;
    private int? _mouseDevice;
    private int? _keyDevice;
    private bool _mouseDiscovered;
    private bool _keyDiscovered;
    private Thread? _relayThread;
    private Action<ReceivedStroke>? _relayObserver;
    private volatile bool _relayStopping;
    private bool _disposed;

    public InterceptionDriver() : this(new InterceptionNativeApi()) { }

    internal InterceptionDriver(IInterceptionApi api, Func<ushort, ushort>? scanCodeMapper = null)
    {
        _api = api;
        _scanCodeMapper = scanCodeMapper ?? User32.MapVirtualKeyToScan;
    }

    public string BackendName => "interception";

    public void Arm(TimeSpan timeout)
    {

        _ = Context;
        _mouseDevice ??= InterceptionConstants.MouseDeviceMin;
        _keyDevice ??= InterceptionConstants.KeyboardDeviceMin;
    }

    public int DiscoverMouse(TimeSpan timeout)
    {
        if (_mouseDiscovered) return _mouseDevice!.Value;

        var device = Discover(DeviceClass.Mouse, timeout);
        _mouseDevice = device;
        _mouseDiscovered = true;
        return device;
    }

    public int DiscoverKey(TimeSpan timeout)
    {
        if (_keyDiscovered) return _keyDevice!.Value;

        var device = Discover(DeviceClass.Keyboard, timeout);
        _keyDevice = device;
        _keyDiscovered = true;
        return device;
    }

    public void StartStrokeRelay(TimeSpan discoveryTimeout, Action<ReceivedStroke> onStroke)
    {
        ArgumentNullException.ThrowIfNull(onStroke);

        lock (_relayGate)
        {
            if (_relayThread is not null)
            {
                throw new InputDriverException("接收-转发循环已在运行——先 StopStrokeRelay() 再重开。");
            }

            DiscoverMouse(discoveryTimeout);
            DiscoverKey(discoveryTimeout);

            var ctx = Context;
            _api.SetFilter(ctx, _mouseDevice!.Value, InterceptionConstants.FilterAll);
            _api.SetFilter(ctx, _keyDevice!.Value, InterceptionConstants.FilterAll);

            _relayObserver = onStroke;
            _relayStopping = false;
            var thread = new Thread(() => RelayLoop(ctx)) { IsBackground = true, Name = "interception-relay" };
            _relayThread = thread;
            thread.Start();
        }
    }

    public void StopStrokeRelay()
    {
        Thread? thread;
        lock (_relayGate)
        {
            thread = _relayThread;
            _relayStopping = true;
        }
        if (thread is null) return;

        thread.Join();

        lock (_relayGate)
        {
            _relayThread = null;
            _relayObserver = null;

            while (_relaySends.TryDequeue(out var send)) send();

            ForwardQueuedStrokes(_context);

            if (_mouseDevice is int mouse) _api.SetFilter(_context, mouse, InterceptionConstants.FilterNone);
            if (_keyDevice is int key) _api.SetFilter(_context, key, InterceptionConstants.FilterNone);
        }
    }

    public void MoveRelative(int dx, int dy)
    {
        var dev = RequireMouseDevice();
        DeliverMouseStroke(dev, new InterceptionMouseStroke { X = dx, Y = dy });
    }

    public void KeyDown(InputKey key) => SendKey(key, down: true);

    public void KeyUp(InputKey key) => SendKey(key, down: false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopStrokeRelay();
        if (_context != IntPtr.Zero)
        {
            _api.DestroyContext(_context);
            _context = IntPtr.Zero;
        }
    }

    private void SendKey(InputKey key, bool down)
    {
        if (key.IsMouse)
        {
            var dev = RequireMouseDevice();
            DeliverMouseStroke(dev, new InterceptionMouseStroke { State = MouseState(key.Mouse, down) });
            return;
        }

        var keyDev = _keyDevice ?? throw new InputDriverException("尚未发现键盘设备——发键盘键前先 DiscoverKey()。");
        DeliverKeyStroke(keyDev, new InterceptionKeyStroke
        {
            Code = _scanCodeMapper(key.VirtualKey),
            State = down ? (ushort)0 : InterceptionConstants.KeyUp,
        });
    }

    private void DeliverMouseStroke(int device, InterceptionMouseStroke stroke)
    {
        if (TryEnqueueRelaySend(() => EnsureSent(_api.SendMouseStroke(_context, device, in stroke), $"mouse→{device}"))) return;
        EnsureSent(_api.SendMouseStroke(Context, device, in stroke), $"mouse→{device}");
    }

    private void DeliverKeyStroke(int device, InterceptionKeyStroke stroke)
    {
        if (TryEnqueueRelaySend(() => EnsureSent(_api.SendKeyStroke(_context, device, in stroke), $"key→{device}"))) return;
        EnsureSent(_api.SendKeyStroke(Context, device, in stroke), $"key→{device}");
    }

    private bool TryEnqueueRelaySend(Action send)
    {
        lock (_relayGate)
        {
            if (_relayThread is null || _relayStopping) return false;
            _relaySends.Enqueue(send);
            return true;
        }
    }

    private void RelayLoop(IntPtr ctx)
    {
        while (!_relayStopping)
        {
            while (_relaySends.TryDequeue(out var send)) send();

            var dev = _api.WaitWithTimeout(ctx, RelayWaitMs);
            if (dev <= 0) continue;

            if (dev >= InterceptionConstants.MouseDeviceMin && dev <= InterceptionConstants.MouseDeviceMax)
            {
                if (_api.ReceiveMouseStroke(ctx, dev, out var stroke) != 1) continue;
                _api.SendMouseStroke(ctx, dev, in stroke);
                _relayObserver?.Invoke(ReceivedStroke.Mouse(stroke.State, stroke.Flags, stroke.Rolling, stroke.X, stroke.Y));
            }
            else
            {
                if (_api.ReceiveKeyStroke(ctx, dev, out var stroke) != 1) continue;
                _api.SendKeyStroke(ctx, dev, in stroke);
                _relayObserver?.Invoke(ReceivedStroke.Key(stroke.Code, stroke.State));
            }
        }
    }

    private void ForwardQueuedStrokes(IntPtr ctx)
    {
        while (_api.WaitWithTimeout(ctx, 0) is int dev && dev > 0)
        {
            if (dev >= InterceptionConstants.MouseDeviceMin && dev <= InterceptionConstants.MouseDeviceMax)
            {
                if (_api.ReceiveMouseStroke(ctx, dev, out var stroke) == 1) _api.SendMouseStroke(ctx, dev, in stroke);
            }
            else if (_api.ReceiveKeyStroke(ctx, dev, out var stroke) == 1)
            {
                _api.SendKeyStroke(ctx, dev, in stroke);
            }
        }
    }

    private static void EnsureSent(int sent, string target)
    {
        if (sent != 1)
        {
            throw new InputDriverException($"interception_send({target}) 返回 0：报告没送进设备栈（驱动掉线？查 sc query interception）。");
        }
    }

    private int RequireMouseDevice() =>
        _mouseDevice ?? throw new InputDriverException("尚未发现鼠标设备——发输入前先 Arm()（设备发现）。");

    private int Discover(DeviceClass cls, TimeSpan timeout)
    {
        var ctx = Context;

        for (var dev = cls.Lo; dev <= cls.Hi; dev++) _api.SetFilter(ctx, dev, InterceptionConstants.FilterAll);
        var fired = _api.WaitWithTimeout(ctx, (uint)Math.Max(0, timeout.TotalMilliseconds));
        var got = fired >= cls.Lo && fired <= cls.Hi;

        if (got)
        {
            if (cls.IsMouse)
            {
                if (_api.ReceiveMouseStroke(ctx, fired, out var mouseStroke) == 1) _api.SendMouseStroke(ctx, fired, in mouseStroke);
            }
            else if (_api.ReceiveKeyStroke(ctx, fired, out var keyStroke) == 1)
            {
                _api.SendKeyStroke(ctx, fired, in keyStroke);
            }
        }

        for (var dev = cls.Lo; dev <= cls.Hi; dev++) _api.SetFilter(ctx, dev, InterceptionConstants.FilterNone);

        if (!got)
        {
            throw new InputDriverException(
                $"Interception 设备发现超时：{timeout.TotalSeconds:0} 秒内没收到{cls.Kind}事件（请{cls.Verb}）。" +
                $"＝驱动服务起了但没挂进设备栈（管理员查注册表{cls.Kind}类 UpperFilters 是否真注册了 interception）≠ 可用。");
        }

        return fired;
    }

    private static ushort MouseState(MouseButton button, bool down) => (button, down) switch
    {
        (MouseButton.Left, true) => InterceptionConstants.MouseLeftDown,
        (MouseButton.Left, false) => InterceptionConstants.MouseLeftUp,
        (MouseButton.Right, true) => InterceptionConstants.MouseRightDown,
        (MouseButton.Right, false) => InterceptionConstants.MouseRightUp,
        (MouseButton.Middle, true) => InterceptionConstants.MouseMiddleDown,
        (MouseButton.Middle, false) => InterceptionConstants.MouseMiddleUp,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "不支持的鼠标键"),
    };

    private IntPtr Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_context != IntPtr.Zero) return _context;

            try
            {
                _context = _api.CreateContext();
            }
            catch (DllNotFoundException ex)
            {
                throw new InputDriverException(
                    "找不到 interception.dll：从 Interception 发布包（GitHub release zip）取 library/x64/interception.dll " +
                    "放到程序输出目录（本程序集默认随带）。dll 只是用户态调用壳，不用再装。", ex);
            }

            if (_context == IntPtr.Zero)
            {
                throw new InputDriverException(
                    "interception_create_context 失败＝驱动没在运行。管理员终端查 sc query interception，" +
                    "STATE 应为 RUNNING；不是＝没装完（ticket 12 手动 sc create 路径）。");
            }

            return _context;
        }
    }
}
