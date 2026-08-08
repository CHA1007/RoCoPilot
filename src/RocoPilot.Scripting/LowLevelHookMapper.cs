namespace RocoPilot.Scripting;

public enum HookMouseKind
{
    Move,
    Button,
    Wheel,
}

public readonly record struct HookMouseEvent(HookMouseKind Kind, ushort State, short Rolling, int X, int Y);

public static class LowLevelHookMapper
{
    public const ushort KeyUpState = 0x001;
    public const ushort KeyExtendedFlag = 0x002;

    public const ushort MouseLeftDown = 0x001;
    public const ushort MouseLeftUp = 0x002;
    public const ushort MouseRightDown = 0x004;
    public const ushort MouseRightUp = 0x008;
    public const ushort MouseMiddleDown = 0x010;
    public const ushort MouseMiddleUp = 0x020;
    public const ushort MouseX1Down = 0x040;
    public const ushort MouseX1Up = 0x080;
    public const ushort MouseX2Down = 0x100;
    public const ushort MouseX2Up = 0x200;
    public const ushort MouseWheel = 0x0400;
    public const ushort MouseHWheel = 0x0800;

    public const ushort RiMouseMoveAbsolute = 0x02;

    public const uint VkTab = 0x09;
    public const uint VkMenu = 0x12;
    public const uint VkLWin = 0x5B;
    public const uint VkRWin = 0x5C;

    public static bool IsSystemNavigationKey(uint vkCode, bool altDown)
        => vkCode is VkLWin or VkRWin or VkMenu || (vkCode == VkTab && altDown);

    public static (int Dx, int Dy) RawMouseMove(ushort flags, int lastX, int lastY, int prevX, int prevY)
        => (flags & RiMouseMoveAbsolute) != 0 ? (lastX - prevX, lastY - prevY) : (lastX, lastY);

    private const uint LlKfUp = 0x80;
    private const uint LlKfExtended = 0x01;

    private const ulong WmMouseMove = 0x0200;
    private const ulong WmLeftButtonDown = 0x0201;
    private const ulong WmLeftButtonUp = 0x0202;
    private const ulong WmRightButtonDown = 0x0204;
    private const ulong WmRightButtonUp = 0x0205;
    private const ulong WmMiddleButtonDown = 0x0207;
    private const ulong WmMiddleButtonUp = 0x0208;
    private const ulong WmMouseWheel = 0x020A;
    private const ulong WmXButtonDown = 0x020B;
    private const ulong WmXButtonUp = 0x020C;
    private const ulong WmMouseHWheel = 0x020E;

    public static (ushort Code, ushort State) MapKey(ushort scanCode, uint flags)
    {
        var state = (flags & LlKfUp) != 0 ? KeyUpState : (ushort)0;
        if ((flags & LlKfExtended) != 0) state |= KeyExtendedFlag;
        return (scanCode, state);
    }

    public static HookMouseEvent? MapMouse(ulong message, int x, int y, uint mouseData) => message switch
    {
        WmMouseMove => new HookMouseEvent(HookMouseKind.Move, 0, 0, x, y),
        WmLeftButtonDown => Button(MouseLeftDown),
        WmLeftButtonUp => Button(MouseLeftUp),
        WmRightButtonDown => Button(MouseRightDown),
        WmRightButtonUp => Button(MouseRightUp),
        WmMiddleButtonDown => Button(MouseMiddleDown),
        WmMiddleButtonUp => Button(MouseMiddleUp),
        WmMouseWheel => new HookMouseEvent(HookMouseKind.Wheel, MouseWheel, WheelDelta(mouseData), 0, 0),
        WmMouseHWheel => new HookMouseEvent(HookMouseKind.Wheel, MouseHWheel, WheelDelta(mouseData), 0, 0),
        WmXButtonDown => XButton(mouseData, down: true),
        WmXButtonUp => XButton(mouseData, down: false),
        _ => null,
    };

    private static HookMouseEvent Button(ushort state) => new(HookMouseKind.Button, state, 0, 0, 0);

    private static HookMouseEvent XButton(uint mouseData, bool down)
    {
        var isFirst = ((mouseData >> 16) & 0xFFFF) == 1;
        var state = (isFirst, down) switch
        {
            (true, true) => MouseX1Down,
            (true, false) => MouseX1Up,
            (false, true) => MouseX2Down,
            (false, false) => MouseX2Up,
        };
        return Button(state);
    }

    private static short WheelDelta(uint mouseData) => (short)(mouseData >> 16);
}