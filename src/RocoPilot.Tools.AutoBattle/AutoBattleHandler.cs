using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Tools.AutoBattle.Battle;

namespace RocoPilot.Tools.AutoBattle;

/// <summary>
/// 自动战斗场景处理器：战斗场景切入时按配置释放技能（v1）。
/// 按键映射：技能面板 R，技能槽 1–4，逃跑 ESC，背包 Q，捕捉 W，更换 E。
/// </summary>
public sealed class AutoBattleHandler : ISceneHandler, IDisposable
{
    private readonly AutoBattleSettings _settings;
    private readonly IBattleSensor _sensor;
    private IBattleAction? _action;
    private SceneContext? _context;

    public AutoBattleHandler(AutoBattleSettings settings, IBattleSensor sensor)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
    }

    public GameScene Scene => GameScene.Battle;

    public bool IsEnabled { get; set; } = true;

    public void Activate(SceneContext context)
    {
        _context = context;
        _settings.SanitizeInPlace();

        _action = new SkillAction(_settings.SkillSlot, _settings.ClickDelayMinMs, _settings.ClickDelayMaxMs);

        context.EmitEvent(new ToolEvent("battle_started", new Dictionary<string, object?>
        {
            ["skill_slot"] = _settings.SkillSlot,
        }));
    }

    public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (_action is null || _context is null)
            return false;

        return _action.Execute(_context.InputDriver, _sensor, bgraPixels, width, height);
    }

    public void Deactivate()
    {
        _context?.EmitEvent(new ToolEvent("battle_stopped"));
        _action = null;
        _context = null;
    }

    public void Dispose() => (_sensor as IDisposable)?.Dispose();
}
