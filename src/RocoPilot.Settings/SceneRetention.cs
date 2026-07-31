namespace RocoPilot.Settings;

public static class SceneRetention
{
    public const int DefaultKeepScenes = 100;

    public static void PruneScenes(string scenesRoot, int keep = DefaultKeepScenes)
    {
        ArgumentException.ThrowIfNullOrEmpty(scenesRoot);
        if (keep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), $"留存数须为正，实得 {keep}");
        }

        if (!Directory.Exists(scenesRoot))
        {
            return;
        }

        var oldestFirst = Directory.GetDirectories(scenesRoot)
            .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToList();
        foreach (var dir in oldestFirst.Take(oldestFirst.Count - keep))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
