namespace RocoPilot.Installer.Core;

public static class ShortcutService
{
    public static void Create(string lnkPath, string targetPath, string workingDirectory, string iconPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("WScript.Shell 不可用，无法创建快捷方式。");
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.GetType().InvokeMember(
            "CreateShortcut",
            System.Reflection.BindingFlags.InvokeMethod,
            null,
            shell,
            new object[] { lnkPath });
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = $"{iconPath},0";
        shortcut.Save();
    }

    public static void DeleteIfExists(string lnkPath)
    {
        if (File.Exists(lnkPath))
        {
            File.Delete(lnkPath);
        }
    }
}