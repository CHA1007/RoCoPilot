using RocoPilot.Input.Native;

namespace RocoPilot.Input.SendInput;

public sealed class SendInputDriver : IInputDriver
{
    private readonly Action<Input> _dispatch;
    private readonly Func<ushort, ushort> _scanMapper;

    public SendInputDriver() : this(SendInputNative.Send) { }

    internal SendInputDriver(Action<Input> dispatch, Func<ushort, ushort>? scanMapper = null)
    {
        _dispatch = dispatch;
        _scanMapper = scanMapper ?? User32.MapVirtualKeyToScan;
    }

    public string BackendName => "sendinput";

    public void Arm(TimeSpan timeout) { }

    public void MoveRelative(int dx, int dy) => _dispatch(SendInputBuilder.Move(dx, dy));

    public void KeyDown(InputKey key) => SendKey(key, down: true);

    public void KeyUp(InputKey key) => SendKey(key, down: false);

    public void Dispose() { }

    private void SendKey(InputKey key, bool down)
    {
        _dispatch(key.IsMouse
            ? SendInputBuilder.MouseButtonReport(key.Mouse, down)
            : SendInputBuilder.Keyboard(key.VirtualKey, _scanMapper(key.VirtualKey), down));
    }
}
