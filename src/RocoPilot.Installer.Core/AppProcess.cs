namespace RocoPilot.Installer.Core;

public static class AppProcess
{
    public static void Terminate()
    {
        foreach (var name in new[] { "RocoPilot", "RocoPilotUninstaller" })
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
    }
}