namespace RocoPilot.Settings;

public static class RocoPaths
{
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RocoPilot");

    public static string SettingsFilePath => Path.Combine(AppDataRoot, "settings.json");

    public static string LocalAppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RocoPilot");

    public static string CacheDirectory => Path.Combine(LocalAppDataRoot, "cache");

    public static string LogsRoot => Path.Combine(LocalAppDataRoot, "logs");
}
