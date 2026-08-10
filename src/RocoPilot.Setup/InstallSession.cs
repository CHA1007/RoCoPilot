using RocoPilot.Installer.Core;

namespace RocoPilot.Setup;

public sealed class InstallSession
{
    public string Version { get; set; } = "0.1.0";

    public string InstallPath { get; set; } = InstallLayout.InstallRoot;

    public bool CreateDesktopShortcut { get; set; } = true;

    public bool LaunchOnExit { get; set; } = true;

    public bool InstallInterceptionDriver { get; set; }

    public bool InterceptionMissing { get; set; }
}