namespace RocoPilot.Settings;

public static class LogRetention
{
    public const int DefaultKeepSessions = 30;

    public static void PruneSessions(string logsRoot, int keep = DefaultKeepSessions)
    {
        ArgumentException.ThrowIfNullOrEmpty(logsRoot);
        if (keep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), $"留存数须为正，实得 {keep}");
        }

        if (!Directory.Exists(logsRoot))
        {
            return;
        }

        var oldestFirst = Directory.GetDirectories(logsRoot)
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
