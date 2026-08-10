namespace RocoPilot.Installer.Core;

public static class InstallLayout
{
    public static string InstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RocoPilot");

    public static string StubExePath => Path.Combine(InstallRoot, "RocoPilot.exe");

    public static string UpdateExePath => Path.Combine(InstallRoot, "Update.exe");

    public static string AppExePath => Path.Combine(InstallRoot, "current", "RocoPilot.exe");

    public static string UninstallerExePath => Path.Combine(InstallRoot, "current", "RocoPilotUninstaller.exe");

    public static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "RocoPilot.lnk");

    public static string DesktopShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "RocoPilot.lnk");
}