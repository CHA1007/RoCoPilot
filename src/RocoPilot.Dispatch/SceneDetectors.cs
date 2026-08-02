namespace RocoPilot.Dispatch;

/// <summary>
/// 场景检测器工厂：基于 1080p 标定坐标创建模板匹配检测器。
/// 搜索区域比模板略大，容忍 UI 微移。
/// </summary>
public static class SceneDetectors
{
    /// <summary>模板图根目录（assets/templates/scene/）。</summary>
    public static string TemplateRoot { get; set; } = "assets/templates/scene";

    /// <summary>创建大世界检测器（左下角聊天图标）。</summary>
    public static TemplateSceneDetector CreateOpenWorld(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        return new TemplateSceneDetector(
            GameScene.OpenWorld,
            Path.Combine(root, "openworld-chat.png"),
            // 搜索区域：(60,940)–(200,1080) 比例化，比模板 46×46 宽裕
            (0.031, 0.870, 0.073, 0.130),
            threshold: 0.75);
    }

    /// <summary>创建战斗检测器（右下角战报 UI）。</summary>
    public static TemplateSceneDetector CreateBattle(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        return new TemplateSceneDetector(
            GameScene.Battle,
            Path.Combine(root, "battle-report.png"),
            // 搜索区域：(1760,730)–(1920,920) 比例化，比模板 64×63 宽裕
            (0.917, 0.676, 0.083, 0.176),
            threshold: 0.75);
    }

    /// <summary>创建全部场景检测器。</summary>
    public static IReadOnlyList<ISceneDetector> CreateAll(string? templateRoot = null) =>
    [
        CreateOpenWorld(templateRoot),
        CreateBattle(templateRoot),
    ];
}
