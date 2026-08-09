using Microsoft.Win32;

namespace RocoPilot.Installer.Core;

public static class RegistryContract
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RocoPilot";

    public static void WriteUninstallEntry(string version, string installRoot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRoot);
        key.SetValue("DisplayName", "RocoPilot");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "RocoPilot");
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("DisplayIcon", Path.Combine(installRoot, "RocoPilot.exe"));
        key.SetValue("UninstallString", $"\"{Path.Combine(installRoot, "RocoPilotUninstaller.exe")}\"");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void RemoveUninstallEntry()
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRoot, throwOnMissingSubKey: false);
    }
}