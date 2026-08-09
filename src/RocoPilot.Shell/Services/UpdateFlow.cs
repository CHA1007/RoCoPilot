using RocoPilot.Settings;

namespace RocoPilot.Shell.Services;

public static class UpdateFlow
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/CHA1007/RoCoPilot/releases/latest";
    private const string ReleasesUrl = "https://api.github.com/repos/CHA1007/RoCoPilot/releases?per_page=1";

    public static async Task CheckAsync(UpdateChannel channel, Action<string> report)
    {
        var label = channel == UpdateChannel.Beta ? "测试版" : "稳定版";
        report($"正在检查{label}更新…");

        try
        {
            if (!AppUpdater.IsInstalled)
            {
                if (channel == UpdateChannel.Beta)
                {
                    await PortableDownloadTestAsync(report);
                }
                else
                {
                    await PortableCheckUpdateAsync(report);
                }
                return;
            }

            var version = await AppUpdater.CheckAsync(channel);
            if (version is null)
            {
                report(channel == UpdateChannel.Beta ? "当前测试版已是最新" : "已是最新版本");
                return;
            }

            report($"发现新版本 {version}，正在下载…");
            await AppUpdater.DownloadAsync(channel);
            report($"{label} {version} 已就绪");

            var choice = System.Windows.MessageBox.Show(
                $"{label} {version} 已下载完成，立即重启以完成更新？",
                "RocoPilot", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (choice == System.Windows.MessageBoxResult.Yes)
            {
                AppUpdater.RestartToApply(channel);
            }
        }
        catch (Exception ex)
        {
            report($"{(channel == UpdateChannel.Beta ? "查询" : "检查")}失败：{ex.GetBaseException().Message}");
        }
    }

    private static async Task PortableCheckUpdateAsync(Action<string> report)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RocoPilot");
        using var resp = await http.GetAsync(LatestReleaseUrl);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            report("暂无稳定版本");
            return;
        }

        resp.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var pageUrl = doc.RootElement.GetProperty("html_url").GetString();

        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var remote))
        {
            report($"无法识别发布版本号：{tag}");
            return;
        }

        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (current is not null && remote > current)
        {
            report($"发现新版本 {remote}，已在浏览器打开下载页");
            if (pageUrl is not null)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pageUrl) { UseShellExecute = true });
            }
        }
        else
        {
            report("已是最新版本");
        }
    }

    private static async Task PortableDownloadTestAsync(Action<string> report)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RocoPilot");
        using var resp = await http.GetAsync(ReleasesUrl);
        resp.EnsureSuccessStatusCode();

        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            report("暂无测试版发布");
            return;
        }

        var latest = doc.RootElement[0];
        var tag = latest.GetProperty("tag_name").GetString() ?? "";
        var pageUrl = latest.GetProperty("html_url").GetString();
        var pre = latest.GetProperty("prerelease").GetBoolean();

        report($"已打开最新{(pre ? "测试" : "稳定")}版 {tag} 下载页");
        if (pageUrl is not null)
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(pageUrl) { UseShellExecute = true });
        }
    }
}