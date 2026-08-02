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

    private readonly IInterceptionApi _api;
    private readonly Func<ushort, ushort> _scanCodeMapper;
    private IntPtr _context = IntPtr.Zero;
    private int? _mouseDevice;
    private int? _keyDevice;
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
        // 创建上下文即验证驱动服务在运行；直接用首个设备，无需等待用户输入
        _ = Context;
        _mouseDevice ??= InterceptionConstants.MouseDeviceMin;
        _keyDevice ??= InterceptionConstants.KeyboardDeviceMin;
    }

    public int DiscoverMouse(TimeSpan timeout) => _mouseDevice ??= Discover(DeviceClass.Mouse, timeout);

    public int DiscoverKey(TimeSpan timeout) => _keyDevice ??= Discover(DeviceClass.Keyboard, timeout);

    public void MoveRelative(int dx, int dy)
    {
        var dev = RequireMouseDevice();
        var mouseStroke = new InterceptionMouseStroke { X = dx, Y = dy };
        EnsureSent(_api.SendMouseStroke(Context, dev, in mouseStroke), $"mouse→{dev}");
    }

    public void KeyDown(InputKey key) => SendKey(key, down: true);

    public void KeyUp(InputKey key) => SendKey(key, down: false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
            var mouseStroke = new InterceptionMouseStroke { State = MouseState(key.Mouse, down) };
            EnsureSent(_api.SendMouseStroke(Context, dev, in mouseStroke), $"mouse→{dev}");
            return;
        }

        var keyDev = _keyDevice ?? throw new InputDriverException("尚未发现键盘设备——发键盘键前先 DiscoverKey()。");
        var keyStroke = new InterceptionKeyStroke
        {
            Code = _scanCodeMapper(key.VirtualKey),
            State = down ? (ushort)0 : InterceptionConstants.KeyUp,
        };
        EnsureSent(_api.SendKeyStroke(Context, keyDev, in keyStroke), $"key→{keyDev}");
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
