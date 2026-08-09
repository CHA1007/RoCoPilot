namespace RocoPilot.Installer.Core;

public static class UserDataPaths
{
    public static string RoamingDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RocoPilot");

    public static string LocalDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RocoPilot");
}