namespace RocoPilot.Tools.AutoBattle.Battle;

public enum BattlePanel
{
    Flee,
    Bag,
    Capture,
    Switch,
    Skill,
}

public interface IBattleSensor
{
    BattlePanel? DetectSelectedPanel(ReadOnlySpan<byte> bgraPixels, int width, int height);

    (int Slot, int ScreenX, int ScreenY)? MatchSkill(
        ReadOnlySpan<byte> bgraPixels, int width, int height, string skillName);
}
