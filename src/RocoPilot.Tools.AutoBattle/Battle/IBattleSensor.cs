namespace RocoPilot.Tools.AutoBattle.Battle;

/// <summary>战斗面板类型。</summary>
public enum BattlePanel
{
    Flee,
    Bag,
    Capture,
    Switch,
    Skill,
}

/// <summary>战斗状态感知：识别当前面板选中状态 + 技能图标匹配。</summary>
public interface IBattleSensor
{
    /// <summary>检测当前选中的面板（null = 无面板选中 / 非战斗画面）。</summary>
    BattlePanel? DetectSelectedPanel(ReadOnlySpan<byte> bgraPixels, int width, int height);

    /// <summary>
    /// 在技能槽区域匹配指定技能图标。
    /// 返回匹配到的槽位索引（0–3）和屏幕坐标；未匹配返回 null。
    /// </summary>
    (int Slot, int ScreenX, int ScreenY)? MatchSkill(
        ReadOnlySpan<byte> bgraPixels, int width, int height, string skillName);
}
