using System.Runtime.InteropServices;

namespace RocoPilot.Input.SendInput;

internal static class SendInputConstants
{
    public const uint InputMouse = 0;
    public const uint InputKeyboard = 1;

    public const uint MouseEventFMove = 0x0001;
    public const uint MouseEventFLeftDown = 0x0002;
    public const uint MouseEventFLeftUp = 0x0004;
    public const uint MouseEventFRightDown = 0x0008;
    public const uint MouseEventFRightUp = 0x0010;
    public const uint MouseEventFMiddleDown = 0x0020;
    public const uint MouseEventFMiddleUp = 0x0040;

    public const uint KeyEventFKeyUp = 0x0002;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint DwFlags;
    public uint Time;
    public UIntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeybdInput
{
    public ushort WVk;
    public ushort WScan;
    public uint DwFlags;
    public uint Time;
    public UIntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MouseInput Mi;
    [FieldOffset(0)] public KeybdInput Ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Type;
    public InputUnion U;
}

internal static class SendInputBuilder
{
    public static Input Move(int dx, int dy) => new()
    {
        Type = SendInputConstants.InputMouse,
        U = new InputUnion { Mi = new MouseInput { Dx = dx, Dy = dy, DwFlags = SendInputConstants.MouseEventFMove } },
    };

    public static Input MouseButtonReport(MouseButton button, bool down) => new()
    {
        Type = SendInputConstants.InputMouse,
        U = new InputUnion { Mi = new MouseInput { DwFlags = MouseFlag(button, down) } },
    };

    public static Input Keyboard(ushort virtualKey, ushort scan, bool down) => new()
    {
        Type = SendInputConstants.InputKeyboard,
        U = new InputUnion
        {
            Ki = new KeybdInput
            {
                WVk = virtualKey,
                WScan = scan,
                DwFlags = down ? 0u : SendInputConstants.KeyEventFKeyUp,
            },
        },
    };

    private static uint MouseFlag(MouseButton button, bool down) => (button, down) switch
    {
        (MouseButton.Left, true) => SendInputConstants.MouseEventFLeftDown,
        (MouseButton.Left, false) => SendInputConstants.MouseEventFLeftUp,
        (MouseButton.Right, true) => SendInputConstants.MouseEventFRightDown,
        (MouseButton.Right, false) => SendInputConstants.MouseEventFRightUp,
        (MouseButton.Middle, true) => SendInputConstants.MouseEventFMiddleDown,
        (MouseButton.Middle, false) => SendInputConstants.MouseEventFMiddleUp,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "不支持的鼠标键"),
    };
}

internal static class SendInputNative
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref Input pInputs, int cbSize);

    public static void Send(Input input)
    {
        var sent = SendInput(nInputs: 1, ref input, cbSize: Marshal.SizeOf<Input>());
        if (sent != 1)
        {
            throw new InputDriverException($"SendInput 注入失败：插入 {sent}/1 条（Win32 错误 {Marshal.GetLastWin32Error()}）。");
        }
    }
}
