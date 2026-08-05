namespace RocoPilot.Tools.AutoBattle;

public sealed class AutoBattleSettings
{
    public int SkillSlot { get; set; } = 1;

    public int ClickDelayMinMs { get; set; } = 200;

    public int ClickDelayMaxMs { get; set; } = 500;

    public void SanitizeInPlace()
    {
        SkillSlot = (int)Math.Clamp(SkillSlot, 1, 4);
        ClickDelayMinMs = (int)Math.Clamp(ClickDelayMinMs, 50, 5000);
        ClickDelayMaxMs = (int)Math.Clamp(ClickDelayMaxMs, ClickDelayMinMs, 5000);
    }
}
