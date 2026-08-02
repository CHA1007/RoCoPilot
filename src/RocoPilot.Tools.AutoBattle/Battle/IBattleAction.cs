using RocoPilot.Input;

namespace RocoPilot.Tools.AutoBattle.Battle;

/// <summary>战斗动作抽象（v1 仅实现 SkillAction，其余预留）。</summary>
public interface IBattleAction
{
    /// <summary>执行一次战斗操作。返回 true = 战斗仍在继续，false = 战斗已结束。</summary>
    bool Execute(IInputDriver driver, IBattleSensor sensor, ReadOnlySpan<byte> bgraPixels, int width, int height);
}
