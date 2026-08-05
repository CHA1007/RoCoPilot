using System.Diagnostics;
using RocoPilot.Input.Native;

namespace RocoPilot.Input.Interception;

public sealed class InterceptionDriver : IInputDriver
{
    private const uint RelayWaitMs = 15;
    private static readonly TimeSpan RelayStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RelayRebuildBackoff = TimeSpan.FromSeconds(1);

    private readonly IInterceptionApi _api;
    private readonly Func<ushort, ushort> _scanCodeMapper;
    private readonly object _relayGate = new();
    private readonly object _sendGate = new();
    private IntPtr _sendContext = IntPtr.Zero;
    private IntPtr _receiveContext = IntPtr.Zero;
    private volatile int _activeMouseDevice;
    private volatile int _activeKeyDevice;
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

    public void Arm()
    {
        lock (_sendGate) EnsureSendContext();
    }

    public void MoveRelative(int dx, int dy)
    {
        var stroke = new InterceptionMouseStroke { X = dx, Y = dy };
        lock (_sendGate) DeliverMouseStroke(in stroke);
    }

    public void KeyDown(InputKey key) => SendKey(key, down: true);

    public void KeyUp(InputKey key) => SendKey(key, down: false);

    public void SendRawStroke(ReceivedStroke stroke)
    {
        lock (_sendGate)
        {
            switch (stroke.Kind)
            {
                case ReceivedDeviceKind.Keyboard:
                    var keyStroke = new InterceptionKeyStroke { Code = stroke.Code, State = stroke.State };
                    DeliverKeyStroke(in keyStroke);
                    break;
                case ReceivedDeviceKind.Mouse:
                    var mouseStroke = new InterceptionMouseStroke
                    {
                        State = stroke.State,
                        Flags = stroke.Flags,
                        Rolling = stroke.Rolling,
                        X = stroke.X,
                        Y = stroke.Y,
                    };
                    DeliverMouseStroke(in mouseStroke);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stroke), stroke.Kind, "未知的 stroke 设备类型。");
            }
        }
    }

    public void StartStrokeRelay(Action<ReceivedStroke> onStroke)
    {
        ArgumentNullException.ThrowIfNull(onStroke);

        lock (_relayGate)
        {
            if (_relayThread is not null)
            {
                throw new InputDriverException("接收-转发循环已在运行——先 StopStrokeRelay() 再重开。");
            }

            _receiveContext = CreateReceiveContext();
            _relayObserver = onStroke;
            _relayStopping = false;
            var thread = new Thread(RelayLoop) { IsBackground = true, Name = "interception-relay" };
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

        thread.Join(RelayStopTimeout);

        lock (_relayGate)
        {
            _relayThread = null;
            _relayObserver = null;

            if (_receiveContext != IntPtr.Zero)
            {
                ForwardQueuedStrokes(_receiveContext);
                _api.DestroyContext(_receiveContext);
                _receiveContext = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopStrokeRelay();

        lock (_sendGate)
        {
            if (_sendContext != IntPtr.Zero)
            {
                _api.DestroyContext(_sendContext);
                _sendContext = IntPtr.Zero;
            }
        }
    }

    internal void ProcessRelayEvent(IntPtr context, int device)
    {
        if (device >= InterceptionConstants.MouseDeviceMin && device <= InterceptionConstants.MouseDeviceMax)
        {
            if (_api.ReceiveMouseStroke(context, device, out var stroke) != 1) return;
            _activeMouseDevice = device;
            _api.SendMouseStroke(context, device, in stroke);
            NotifyObserver(ReceivedStroke.Mouse(stroke.State, stroke.Flags, stroke.Rolling, stroke.X, stroke.Y));
            return;
        }

        if (device >= InterceptionConstants.KeyboardDeviceMin && device <= InterceptionConstants.KeyboardDeviceMax)
        {
            if (_api.ReceiveKeyStroke(context, device, out var stroke) != 1) return;
            _activeKeyDevice = device;
            _api.SendKeyStroke(context, device, in stroke);
            NotifyObserver(ReceivedStroke.Key(stroke.Code, stroke.State));
        }
    }

    private void RelayLoop()
    {
        while (!_relayStopping)
        {
            IntPtr context;
            lock (_relayGate) context = _receiveContext;
            if (context == IntPtr.Zero)
            {
                Thread.Sleep(RelayRebuildBackoff);
                continue;
            }

            int device;
            try
            {
                device = _api.WaitWithTimeout(context, RelayWaitMs);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Interception 等待抛异常，重建接收 context：{ex.GetBaseException().Message}");
                RebuildReceiveContext();
                continue;
            }

            if (device <= 0) continue;

            try
            {
                ProcessRelayEvent(context, device);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Interception 中继事件处理抛异常，重建接收 context：{ex.GetBaseException().Message}");
                RebuildReceiveContext();
            }
        }
    }

    private void RebuildReceiveContext()
    {
        lock (_relayGate)
        {
            if (_relayStopping || _relayThread is null) return;

            if (_receiveContext != IntPtr.Zero)
            {
                try
                {
                    _api.DestroyContext(_receiveContext);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Interception 销毁旧接收 context 抛异常（忽略）：{ex.GetBaseException().Message}");
                }
                _receiveContext = IntPtr.Zero;
            }

            try
            {
                _receiveContext = CreateReceiveContext();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Interception 重建接收 context 失败，稍后重试：{ex.GetBaseException().Message}");
            }
        }

        if (_receiveContext == IntPtr.Zero) Thread.Sleep(RelayRebuildBackoff);
    }

    private IntPtr CreateReceiveContext()
    {
        var context = CreateContext();
        for (var device = InterceptionConstants.KeyboardDeviceMin; device <= InterceptionConstants.MouseDeviceMax; device++)
        {
            _api.SetFilter(context, device, InterceptionConstants.FilterAll);
        }

        return context;
    }

    private void ForwardQueuedStrokes(IntPtr context)
    {
        while (_api.WaitWithTimeout(context, 0) is int device && device > 0)
        {
            if (device >= InterceptionConstants.MouseDeviceMin && device <= InterceptionConstants.MouseDeviceMax)
            {
                if (_api.ReceiveMouseStroke(context, device, out var stroke) == 1) _api.SendMouseStroke(context, device, in stroke);
            }
            else if (_api.ReceiveKeyStroke(context, device, out var stroke) == 1)
            {
                _api.SendKeyStroke(context, device, in stroke);
            }
        }
    }

    private void NotifyObserver(ReceivedStroke stroke)
    {
        var observer = _relayObserver;
        if (observer is null) return;

        try
        {
            observer(stroke);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Interception 观察者回调抛异常（已隔离，输入中继不受影响）：{ex.GetBaseException().Message}");
        }
    }

    private void SendKey(InputKey key, bool down)
    {
        lock (_sendGate)
        {
            if (key.IsMouse)
            {
                var mouseStroke = new InterceptionMouseStroke { State = MouseState(key.Mouse, down) };
                DeliverMouseStroke(in mouseStroke);
                return;
            }

            var keyStroke = new InterceptionKeyStroke
            {
                Code = _scanCodeMapper(key.VirtualKey),
                State = down ? (ushort)0 : InterceptionConstants.KeyUp,
            };
            DeliverKeyStroke(in keyStroke);
        }
    }

    private void DeliverMouseStroke(in InterceptionMouseStroke stroke)
    {
        var context = EnsureSendContext();
        var active = _activeMouseDevice;
        if (active != 0 && _api.SendMouseStroke(context, active, in stroke) == 1) return;

        for (var device = InterceptionConstants.MouseDeviceMin; device <= InterceptionConstants.MouseDeviceMax; device++)
        {
            if (_api.SendMouseStroke(context, device, in stroke) == 1)
            {
                _activeMouseDevice = device;
                return;
            }
        }

        throw new InputDriverException(
            "鼠标注入失败：11~20 号设备全部拒收。管理员终端查 sc query interception，" +
            "STATE 应为 RUNNING；不是＝驱动没装完或没挂进设备栈。");
    }

    private void DeliverKeyStroke(in InterceptionKeyStroke stroke)
    {
        var context = EnsureSendContext();
        var active = _activeKeyDevice;
        if (active != 0 && _api.SendKeyStroke(context, active, in stroke) == 1) return;

        for (var device = InterceptionConstants.KeyboardDeviceMin; device <= InterceptionConstants.KeyboardDeviceMax; device++)
        {
            if (_api.SendKeyStroke(context, device, in stroke) == 1)
            {
                _activeKeyDevice = device;
                return;
            }
        }

        throw new InputDriverException(
            "键盘注入失败：1~10 号设备全部拒收。管理员终端查 sc query interception，" +
            "STATE 应为 RUNNING；不是＝驱动没装完或没挂进设备栈。");
    }

    private IntPtr EnsureSendContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sendContext != IntPtr.Zero) return _sendContext;

        _sendContext = CreateContext();
        return _sendContext;
    }

    private IntPtr CreateContext()
    {
        IntPtr context;
        try
        {
            context = _api.CreateContext();
        }
        catch (DllNotFoundException ex)
        {
            throw new InputDriverException(
                "找不到 interception.dll：从 Interception 发布包（GitHub release zip）取 library/x64/interception.dll " +
                "放到程序输出目录（本程序集默认随带）。dll 只是用户态调用壳，不用再装。", ex);
        }

        if (context == IntPtr.Zero)
        {
            throw new InputDriverException(
                "interception_create_context 失败＝驱动没在运行。管理员终端查 sc query interception，" +
                "STATE 应为 RUNNING；不是＝没装完（ticket 12 手动 sc create 路径）。");
        }

        return context;
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
}
