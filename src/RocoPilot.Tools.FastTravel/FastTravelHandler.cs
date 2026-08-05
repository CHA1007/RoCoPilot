using RocoPilot.Core;
using RocoPilot.Dispatch;

namespace RocoPilot.Tools.FastTravel;

public sealed class FastTravelHandler : ISceneHandler
{
    private readonly FastTravelSettings _settings;
    private readonly TeleportSensor? _sensor;
    private readonly Func<int, int, (int X, int Y)> _frameToScreen;

    private TeleportButtonLink? _link;
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
        _settings.SanitizeInPlace();
        _link = _sensor is null
            ? null
            : new TeleportButtonLink(_sensor, context.InputDriver, _settings.ClickCooldownMs, _frameToScreen, context.EmitEvent);

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
        => _link?.TryClick(bgraPixels, width, height) ?? false;

    public void Deactivate()
    {
        _link = null;
    }
}
