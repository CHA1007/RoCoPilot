using RocoPilot.Input;

namespace RocoPilot.Tools.AutoBattle.Battle;

/// <summary>技能释放：按 R 打开技能面板 → 按 1/2/3/4 释放对应技能。</summary>
public sealed class SkillAction : IBattleAction
{
    private readonly int _skillSlot;
    private readonly Random _random = new();
    private readonly int _clickDelayMinMs;
    private readonly int _clickDelayMaxMs;

    /// <param name="skillSlot">技能槽位 1–4。</param>
    public SkillAction(int skillSlot, int clickDelayMinMs = 200, int clickDelayMaxMs = 500)
    {
        _skillSlot = Math.Clamp(skillSlot, 1, 4);
        _clickDelayMinMs = Math.Max(50, clickDelayMinMs);
        _clickDelayMaxMs = Math.Max(_clickDelayMinMs, clickDelayMaxMs);
    }

    public bool Execute(IInputDriver driver, IBattleSensor sensor, ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        // 按 R 打开技能面板
        driver.KeyPress(InputKey.Keyboard(0x52)); // R
        Thread.Sleep(RandomDelay());

        // 按对应数字键释放技能
        var key = (ushort)(0x30 + _skillSlot); // '1'–'4'
        driver.KeyPress(InputKey.Keyboard(key));
        Thread.Sleep(RandomDelay());

        return true; // 战斗是否结束由调度器场景检测判定
    }

    private int RandomDelay() => _random.Next(_clickDelayMinMs, _clickDelayMaxMs + 1);
}
