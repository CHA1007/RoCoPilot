namespace RocoPilot.Settings;

internal static class DirectoryRetention
{
    internal static void Prune(string root, int keep)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (keep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), $"留存数须为正，实得 {keep}");
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        var oldestFirst = Directory.GetDirectories(root)
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
