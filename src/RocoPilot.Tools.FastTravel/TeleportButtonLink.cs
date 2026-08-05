using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Tools.FastTravel;

public sealed class TeleportButtonLink
{
    private readonly TeleportSensor _sensor;
    private readonly IInputDriver _inputDriver;
    private readonly Func<int, int, (int X, int Y)> _frameToScreen;
    private readonly Action<ToolEvent>? _emitEvent;
    private readonly Random _random = new();

    private readonly int _cooldownMs;
    private long _cooldownUntilMs;

    public TeleportButtonLink(
        TeleportSensor sensor,
        IInputDriver inputDriver,
        int cooldownMs,
        Func<int, int, (int X, int Y)>? frameToScreen = null,
        Action<ToolEvent>? emitEvent = null)
    {
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
        _inputDriver = inputDriver ?? throw new ArgumentNullException(nameof(inputDriver));
        _cooldownMs = Math.Max(0, cooldownMs);
        _frameToScreen = frameToScreen ?? ((x, y) => (x, y));
        _emitEvent = emitEvent;
    }

    public void ResetCooldown() => _cooldownUntilMs = 0;

    public bool TryClick(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (Environment.TickCount64 < _cooldownUntilMs)
            return false;

        var hit = _sensor.Find(bgraPixels, width, height);
        if (hit is null)
            return false;

        var (sx, sy) = _frameToScreen(hit.Value.X, hit.Value.Y);
        _inputDriver.ClickAt(sx + _random.Next(-4, 5), sy + _random.Next(-4, 5));

        _cooldownUntilMs = Environment.TickCount64 + _cooldownMs;
        _emitEvent?.Invoke(new ToolEvent("teleport_clicked", new Dictionary<string, object?>
        {
            ["x"] = sx,
            ["y"] = sy,
        }));
        return true;
    }
}
