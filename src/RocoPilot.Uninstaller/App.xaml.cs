using System.Diagnostics;
using System.IO;
using System.Windows;
using RocoPilot.Installer.Core;

namespace RocoPilot.Uninstaller;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (IsRunningFromInstallation())
        {
            RelocateToTemp();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static bool IsRunningFromInstallation()
    {
        return Path.GetFullPath(AppContext.BaseDirectory)
            .StartsWith(InstallLayout.InstallRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void RelocateToTemp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RocoPilotUninstaller", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
        {
            File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(tempDir, "RocoPilotUninstaller.exe"),
            UseShellExecute = true,
        });
    }
}