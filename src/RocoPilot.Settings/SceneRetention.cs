namespace RocoPilot.Settings;

public static class SceneRetention
{
    public const int DefaultKeepScenes = 100;

    public static void PruneScenes(string scenesRoot, int keep = DefaultKeepScenes)
        => DirectoryRetention.Prune(scenesRoot, keep);
}
