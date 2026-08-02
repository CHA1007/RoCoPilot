namespace RocoPilot.Settings;

public static class LogRetention
{
    public const int DefaultKeepSessions = 30;

    public static void PruneSessions(string logsRoot, int keep = DefaultKeepSessions)
        => DirectoryRetention.Prune(logsRoot, keep);
}
