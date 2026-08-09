using RocoPilot.Settings;
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

    public static UpdateManager Manager(UpdateChannel channel) =>
        channel == UpdateChannel.Beta ? Beta.Value : Stable.Value;

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

    public static void ApplyPendingUpdate(UpdateChannel channel)
    {
        var manager = Manager(channel);
        if (!IsInstalled || manager.UpdatePendingRestart is null)
        {
            return;
        }

        manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart);
    }

    public static async Task<string?> CheckAsync(UpdateChannel channel)
    {
        var info = await Manager(channel).CheckForUpdatesAsync();
        return info?.TargetFullRelease.Version.ToString();
    }

    public static async Task<string> DownloadAsync(UpdateChannel channel)
    {
        var manager = Manager(channel);
        var info = await manager.CheckForUpdatesAsync()
            ?? throw new InvalidOperationException("没有待下载的更新");
        await manager.DownloadUpdatesAsync(info);
        return info.TargetFullRelease.Version.ToString();
    }

    public static void RestartToApply(UpdateChannel channel)
    {
        var manager = Manager(channel);
        if (manager.UpdatePendingRestart is { } pending)
        {
            manager.ApplyUpdatesAndRestart(pending);
        }
    }
}