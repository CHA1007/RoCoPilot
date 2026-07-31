namespace RocoPilot.Input;

public enum MouseButton : ushort
{
    Left = 0x01,
    Right = 0x02,
    Middle = 0x04,
}

public readonly record struct InputKey
{
    private readonly ushort _virtualKey;
    private readonly MouseButton? _mouse;

    private InputKey(ushort virtualKey, MouseButton? mouse)
    {
        _virtualKey = virtualKey;
        _mouse = mouse;
    }

    public ushort VirtualKey => _virtualKey;

    public bool IsMouse => _mouse is not null;

    public MouseButton Mouse => _mouse ?? throw new InvalidOperationException("该键不是鼠标键");

    public static InputKey Keyboard(ushort virtualKey) => new(virtualKey, mouse: null);

    public static InputKey FromMouse(MouseButton button) => new((ushort)button, button);

    public static InputKey LeftMouse => FromMouse(MouseButton.Left);
    public static InputKey RightMouse => FromMouse(MouseButton.Right);
    public static InputKey MiddleMouse => FromMouse(MouseButton.Middle);

    public static InputKey Parse(string name) => KeyNames.Parse(name);
}
