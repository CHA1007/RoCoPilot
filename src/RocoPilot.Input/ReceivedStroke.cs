namespace RocoPilot.Input;

public enum ReceivedDeviceKind
{
    Keyboard,
    Mouse,
}

public sealed record ReceivedStroke
{
    public ReceivedDeviceKind Kind { get; init; }

    public ushort Code { get; init; }

    public ushort State { get; init; }

    public ushort Flags { get; init; }

    public short Rolling { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public static ReceivedStroke Key(ushort scanCode, ushort state) => new()
    {
        Kind = ReceivedDeviceKind.Keyboard,
        Code = scanCode,
        State = state,
    };

    public static ReceivedStroke Mouse(ushort state, ushort flags, short rolling, int x, int y) => new()
    {
        Kind = ReceivedDeviceKind.Mouse,
        State = state,
        Flags = flags,
        Rolling = rolling,
        X = x,
        Y = y,
    };
}
