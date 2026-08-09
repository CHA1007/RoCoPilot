namespace RocoPilot.Installer.Core;

public static class InstallLayout
{
    public static string InstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "RocoPilot");

    public static string AppExePath => Path.Combine(InstallRoot, "RocoPilot.exe");

    public static string UninstallerExePath => Path.Combine(InstallRoot, "RocoPilotUninstaller.exe");

    public static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "RocoPilot.lnk");

    public static string DesktopShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "RocoPilot.lnk");
}