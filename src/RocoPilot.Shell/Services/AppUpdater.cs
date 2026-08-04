using Velopack;
using Velopack.Sources;

namespace RocoPilot.Shell.Services;

public static class AppUpdater
{
    private const string RepoUrl = "https://github.com/CHA1007/RoCoPilot";

    private static readonly Lazy<UpdateManager> Stable =
        new(() => new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false)));

    private static readonly Lazy<UpdateManager> Beta =
        new(() => new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: true)));

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return Stable.Value.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void ApplyPendingUpdate()
    {
        if (!IsInstalled || !Stable.Value.IsUpdatePendingRestart)
        {
            return;
        }

        Stable.Value.ApplyUpdatesAndRestart(Stable.Value.UpdatePendingRestart);
    }

    public static async Task<string?> CheckAsync(bool beta)
    {
        var info = await Manager(beta).CheckForUpdatesAsync();
        return info?.TargetFullRelease.Version.ToString();
    }

    public static async Task<string> DownloadAsync(bool beta)
    {
        var manager = Manager(beta);
        var info = await manager.CheckForUpdatesAsync()
            ?? throw new InvalidOperationException("没有待下载的更新");
        await manager.DownloadUpdatesAsync(info);
        return info.TargetFullRelease.Version.ToString();
    }

    public static void RestartToApply()
    {
        var manager = Stable.Value;
        if (manager.UpdatePendingRestart is { } pending)
        {
            manager.ApplyUpdatesAndRestart(pending);
        }
    }

    private static UpdateManager Manager(bool beta) => beta ? Beta.Value : Stable.Value;
}
