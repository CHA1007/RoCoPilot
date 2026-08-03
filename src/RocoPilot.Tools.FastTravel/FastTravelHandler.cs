using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;

namespace RocoPilot.Tools.FastTravel;

public sealed class FastTravelHandler : ISceneHandler
{
    private readonly FastTravelSettings _settings;
    private readonly TeleportSensor? _sensor;
    private readonly Func<int, int, (int X, int Y)> _frameToScreen;
    private readonly Random _random = new();

    private SceneContext? _context;
    private long _cooldownUntilMs;
    private bool _missingTemplateReported;

    public FastTravelHandler(
        FastTravelSettings settings,
        TeleportSensor? sensor,
        Func<int, int, (int X, int Y)>? frameToScreen = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sensor = sensor;
        _frameToScreen = frameToScreen ?? ((x, y) => (x, y));
    }

    public GameScene Scene => GameScene.WorldMap;

    public bool IsEnabled { get; set; } = true;

    public void Activate(SceneContext context)
    {
        _context = context;
        _settings.SanitizeInPlace();
        _cooldownUntilMs = 0;

        if (_sensor is null && !_missingTemplateReported)
        {
            _missingTemplateReported = true;
            context.EmitEvent(new ToolEvent("fault", new Dictionary<string, object?>
            {
                ["error"] = "地图快传模板缺失",
                ["remedy"] = "请放置 assets/templates/map/teleport.png（右下角传送按钮截图）",
            }));
        }
    }

    public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (_context is null || _sensor is null)
            return false;

        if (Environment.TickCount64 < _cooldownUntilMs)
            return false;

        var hit = _sensor.Find(bgraPixels, width, height);
        if (hit is null)
            return false;

        var (sx, sy) = _frameToScreen(hit.Value.X, hit.Value.Y);
        _context.InputDriver.ClickAt(sx + _random.Next(-4, 5), sy + _random.Next(-4, 5));

        _cooldownUntilMs = Environment.TickCount64 + _settings.ClickCooldownMs;
        _context.EmitEvent(new ToolEvent("teleport_clicked", new Dictionary<string, object?>
        {
            ["x"] = sx,
            ["y"] = sy,
        }));
        return true;
    }

    public void Deactivate()
    {
        _context = null;
    }
}
