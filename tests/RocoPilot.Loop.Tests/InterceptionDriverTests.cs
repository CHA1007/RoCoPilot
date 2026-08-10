using RocoPilot.Input;
using RocoPilot.Input.Interception;

namespace RocoPilot.Loop.Tests;

internal sealed class FakeInterceptionApi : IInterceptionApi
{
    private int _nextContextId = 0x1000;

    public bool CreateContextFails { get; set; }

    public List<IntPtr> CreatedContexts { get; } = [];

    public List<IntPtr> DestroyedContexts { get; } = [];

    public List<(IntPtr Context, int Device, ushort Mask)> Filters { get; } = [];

    public List<(IntPtr Context, int Device, InterceptionMouseStroke Stroke)> MouseSends { get; } = [];

    public List<(IntPtr Context, int Device, InterceptionKeyStroke Stroke)> KeySends { get; } = [];

    public HashSet<int> AcceptingMouseDevices { get; } = [];

    public HashSet<int> AcceptingKeyDevices { get; } = [];

    public Queue<int> WaitResults { get; } = new();

    public Dictionary<int, InterceptionMouseStroke> MouseStrokesByDevice { get; } = new();

    public Dictionary<int, InterceptionKeyStroke> KeyStrokesByDevice { get; } = new();

    public IntPtr CreateContext()
    {
        if (CreateContextFails) return IntPtr.Zero;
        var context = new IntPtr(_nextContextId++);
        CreatedContexts.Add(context);
        return context;
    }

    public void DestroyContext(IntPtr context) => DestroyedContexts.Add(context);

    public void SetFilter(IntPtr context, int device, ushort filterMask) => Filters.Add((context, device, filterMask));

    public int WaitWithTimeout(IntPtr context, uint timeoutMs) => WaitResults.TryDequeue(out var device) ? device : 0;

    public int ReceiveMouseStroke(IntPtr context, int device, out InterceptionMouseStroke stroke)
    {
        if (MouseStrokesByDevice.TryGetValue(device, out stroke)) return 1;
        stroke = default;
        return 0;
    }

    public int SendMouseStroke(IntPtr context, int device, in InterceptionMouseStroke stroke)
    {
        MouseSends.Add((context, device, stroke));
        return AcceptingMouseDevices.Contains(device) ? 1 : 0;
    }

    public int ReceiveKeyStroke(IntPtr context, int device, out InterceptionKeyStroke stroke)
    {
        if (KeyStrokesByDevice.TryGetValue(device, out stroke)) return 1;
        stroke = default;
        return 0;
    }

    public int SendKeyStroke(IntPtr context, int device, in InterceptionKeyStroke stroke)
    {
        KeySends.Add((context, device, stroke));
        return AcceptingKeyDevices.Contains(device) ? 1 : 0;
    }
}

public sealed class InterceptionDriverTests
{
    private static ushort FixedScanMap(ushort virtualKey) => (ushort)(virtualKey + 0x100);

    private static (InterceptionDriver Driver, FakeInterceptionApi Api) CreateDriver()
    {
        var api = new FakeInterceptionApi();
        var driver = new InterceptionDriver(api, FixedScanMap);
        return (driver, api);
    }

    [Fact]
    public void CanUse_ReturnsTrue_WhenDriverRunning()
    {
        var api = new FakeInterceptionApi();

        Assert.True(InterceptionDriver.CanUse(api));
    }

    [Fact]
    public void CanUse_ReturnsFalse_WhenDriverNotRunning()
    {
        var api = new FakeInterceptionApi { CreateContextFails = true };

        Assert.False(InterceptionDriver.CanUse(api));
    }

    [Fact]
    public void Arm_CreatesSendContextOnce()
    {
        var (driver, api) = CreateDriver();

        driver.Arm();
        driver.Arm();

        Assert.Single(api.CreatedContexts);
    }

    [Fact]
    public void Arm_Throws_WhenDriverNotRunning()
    {
        var (driver, api) = CreateDriver();
        api.CreateContextFails = true;

        var ex = Assert.Throws<InputDriverException>(() => driver.Arm());
        Assert.Contains("sc query interception", ex.Message);
    }

    [Fact]
    public void MoveRelative_ResolvesFirstAcceptingMouseDevice()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingMouseDevices.Add(13);

        driver.MoveRelative(7, -3);

        Assert.Equal([11, 12, 13], api.MouseSends.Select(s => s.Device).ToArray());
        Assert.Equal(7, api.MouseSends[^1].Stroke.X);
        Assert.Equal(-3, api.MouseSends[^1].Stroke.Y);
    }

    [Fact]
    public void MoveRelative_SticksToActiveDevice()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingMouseDevices.Add(13);
        api.AcceptingMouseDevices.Add(17);

        driver.MoveRelative(1, 1);
        api.MouseSends.Clear();
        driver.MoveRelative(2, 2);

        Assert.Equal([13], api.MouseSends.Select(s => s.Device).ToArray());
    }

    [Fact]
    public void MoveRelative_ReResolves_WhenActiveDeviceStopsAccepting()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingMouseDevices.Add(13);
        driver.MoveRelative(1, 1);

        api.AcceptingMouseDevices.Remove(13);
        api.AcceptingMouseDevices.Add(17);
        api.MouseSends.Clear();
        driver.MoveRelative(2, 2);

        Assert.Equal(13, api.MouseSends[0].Device);
        Assert.Equal(17, api.MouseSends[^1].Device);
    }

    [Fact]
    public void MoveRelative_Throws_WhenNoMouseDeviceAccepts()
    {
        var (driver, _) = CreateDriver();

        var ex = Assert.Throws<InputDriverException>(() => driver.MoveRelative(1, 1));
        Assert.Contains("鼠标注入失败", ex.Message);
    }

    [Fact]
    public void KeyDown_ResolvesFirstAcceptingKeyDevice_AndMapsScanCode()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingKeyDevices.Add(2);

        driver.KeyDown(InputKey.Parse("space"));

        Assert.Equal([1, 2], api.KeySends.Select(s => s.Device).ToArray());
        Assert.Equal(FixedScanMap(InputKey.Parse("space").VirtualKey), api.KeySends[^1].Stroke.Code);
        Assert.Equal((ushort)0, api.KeySends[^1].Stroke.State);
    }

    [Fact]
    public void KeyUp_SendsKeyUpState()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingKeyDevices.Add(1);

        driver.KeyUp(InputKey.Parse("space"));

        Assert.Equal(InterceptionConstants.KeyUp, api.KeySends[^1].Stroke.State);
    }

    [Fact]
    public void KeyDown_MouseKey_GoesToMouseDevice()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingMouseDevices.Add(11);

        driver.KeyDown(InputKey.LeftMouse);

        Assert.Equal(InterceptionConstants.MouseLeftDown, api.MouseSends[^1].Stroke.State);
        Assert.Empty(api.KeySends);
    }

    [Fact]
    public void SendRawStroke_Keyboard_DeliversToDeviceRange()
    {
        var (driver, api) = CreateDriver();
        api.AcceptingKeyDevices.Add(5);

        driver.SendRawStroke(ReceivedStroke.Key(0x39, 0));

        Assert.Equal([1, 2, 3, 4, 5], api.KeySends.Select(s => s.Device).ToArray());
    }

    [Fact]
    public void StartStrokeRelay_FiltersAllDevicesOnReceiveContext()
    {
        var (driver, api) = CreateDriver();

        driver.StartStrokeRelay(_ => { });
        driver.StopStrokeRelay();

        var receiveContext = api.CreatedContexts[^1];
        var filters = api.Filters.Where(f => f.Context == receiveContext).ToArray();
        Assert.Equal(20, filters.Length);
        Assert.All(filters, f => Assert.Equal(InterceptionConstants.FilterAll, f.Mask));
        Assert.Contains(receiveContext, api.DestroyedContexts);
    }

    [Fact]
    public void ProcessRelayEvent_Mouse_TracksDeviceForwardsStrokeAndNotifies()
    {
        var (driver, api) = CreateDriver();
        ReceivedStroke? observed = null;
        driver.StartStrokeRelay(stroke => observed = stroke);
        var receiveContext = api.CreatedContexts[^1];

        api.MouseStrokesByDevice[13] = new InterceptionMouseStroke { State = 1, X = 5, Y = -6, Rolling = 1 };
        driver.ProcessRelayEvent(receiveContext, 13);

        Assert.NotNull(observed);
        Assert.Equal(ReceivedDeviceKind.Mouse, observed!.Kind);
        Assert.Equal(5, observed.X);
        Assert.Equal(-6, observed.Y);
        Assert.Contains(api.MouseSends, s => s.Device == 13 && s.Stroke.X == 5);

        api.AcceptingMouseDevices.Add(13);
        api.MouseSends.Clear();
        driver.MoveRelative(9, 9);
        Assert.Equal([13], api.MouseSends.Select(s => s.Device).ToArray());

        driver.StopStrokeRelay();
    }

    [Fact]
    public void ProcessRelayEvent_Key_TracksDeviceAndNotifies()
    {
        var (driver, api) = CreateDriver();
        ReceivedStroke? observed = null;
        driver.StartStrokeRelay(stroke => observed = stroke);
        var receiveContext = api.CreatedContexts[^1];

        api.KeyStrokesByDevice[4] = new InterceptionKeyStroke { Code = 0x39, State = 0 };
        driver.ProcessRelayEvent(receiveContext, 4);

        Assert.NotNull(observed);
        Assert.Equal(ReceivedDeviceKind.Keyboard, observed!.Kind);
        Assert.Equal(0x39, observed.Code);
        Assert.Contains(api.KeySends, s => s.Device == 4 && s.Stroke.Code == 0x39);

        driver.StopStrokeRelay();
    }

    [Fact]
    public void ProcessRelayEvent_ObserverException_DoesNotPropagateOrDropForwarding()
    {
        var (driver, api) = CreateDriver();
        driver.StartStrokeRelay(_ => throw new InvalidOperationException("观察者炸了"));
        var receiveContext = api.CreatedContexts[^1];

        api.MouseStrokesByDevice[12] = new InterceptionMouseStroke { X = 3 };
        var exception = Record.Exception(() => driver.ProcessRelayEvent(receiveContext, 12));

        Assert.Null(exception);
        Assert.Contains(api.MouseSends, s => s.Device == 12 && s.Stroke.X == 3);

        driver.StopStrokeRelay();
    }

    [Fact]
    public void StartStrokeRelay_Twice_Throws()
    {
        var (driver, _) = CreateDriver();

        driver.StartStrokeRelay(_ => { });
        Assert.Throws<InputDriverException>(() => driver.StartStrokeRelay(_ => { }));

        driver.StopStrokeRelay();
    }

    [Fact]
    public void Dispose_DestroysSendContext()
    {
        var (driver, api) = CreateDriver();
        driver.Arm();
        var sendContext = api.CreatedContexts[^1];

        driver.Dispose();

        Assert.Contains(sendContext, api.DestroyedContexts);
    }
}
