namespace RocoPilot.Tools.AutoBattle;

/// <summary>自动战斗配置。</summary>
public sealed class AutoBattleSettings
{
    /// <summary>使用的技能槽位（1–4）。</summary>
    public int SkillSlot { get; set; } = 1;

    /// <summary>按键间随机延迟下限（ms）。</summary>
    public int ClickDelayMinMs { get; set; } = 200;

    /// <summary>按键间随机延迟上限（ms）。</summary>
    public int ClickDelayMaxMs { get; set; } = 500;

    /// <summary>输入后端。</summary>
    public string InputBackend { get; set; } = "interception";

    public void SanitizeInPlace()
    {
        SkillSlot = (int)Math.Clamp(SkillSlot, 1, 4);
        ClickDelayMinMs = (int)Math.Clamp(ClickDelayMinMs, 50, 5000);
        ClickDelayMaxMs = (int)Math.Clamp(ClickDelayMaxMs, ClickDelayMinMs, 5000);
    }
}
