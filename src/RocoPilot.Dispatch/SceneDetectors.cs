namespace RocoPilot.Dispatch;

public static class SceneDetectors
{
    public static string TemplateRoot { get; set; } = "assets/templates/scene";

    public static TemplateSceneDetector CreateOpenWorld(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        return new TemplateSceneDetector(
            GameScene.OpenWorld,
            Path.Combine(root, "openworld-chat.png"),

            (0.031, 0.870, 0.073, 0.130),
            threshold: 0.75);
    }

    public static TemplateSceneDetector CreateBattle(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        return new TemplateSceneDetector(
            GameScene.Battle,
            Path.Combine(root, "battle-report.png"),

            (0.917, 0.676, 0.083, 0.176),
            threshold: 0.75);
    }

    public static TemplateSceneDetector? CreateWorldMap(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        var path = Path.Combine(root, "map-close.png");
        if (!File.Exists(path))
            return null;

        return new TemplateSceneDetector(
            GameScene.WorldMap,
            path,

            (0.90, 0.00, 0.10, 0.14),
            threshold: 0.75);
    }

    public static TemplateSceneDetector? CreateWorldMapPanel(string? templateRoot = null)
    {
        var root = templateRoot ?? TemplateRoot;
        var path = Path.Combine(root, "map-panel-close.png");
        if (!File.Exists(path))
            return null;

        return new TemplateSceneDetector(
            GameScene.WorldMap,
            path,

            (0.90, 0.00, 0.10, 0.14),
            threshold: 0.75);
    }

    public static IReadOnlyList<ISceneDetector> CreateAll(string? templateRoot = null)
    {
        var detectors = new List<ISceneDetector> { CreateOpenWorld(templateRoot), CreateBattle(templateRoot) };
        if (CreateWorldMap(templateRoot) is { } worldMap)
            detectors.Add(worldMap);
        if (CreateWorldMapPanel(templateRoot) is { } worldMapPanel)
            detectors.Add(worldMapPanel);
        return detectors;
    }
}
