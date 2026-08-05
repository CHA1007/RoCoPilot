using System.Windows;
using System.Windows.Controls;
using RocoPilot.Input;
using RocoPilot.Input.Interception;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class SettingsPage : Page
{
    private readonly ISettingsStore _store;
    private readonly CaptureHost _capture;
    private bool _suppressThemeEvent;

    private const string LatestReleaseUrl = "https://api.github.com/repos/CHA1007/RoCoPilot/releases/latest";
    private const string ReleasesUrl = "https://api.github.com/repos/CHA1007/RoCoPilot/releases?per_page=1";

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "正在检查更新…";

        try
        {
            if (!AppUpdater.IsInstalled)
            {
                await PortableCheckUpdateAsync();
                return;
            }

            var version = await AppUpdater.CheckAsync(beta: false);
            if (version is null)
            {
                UpdateStatusText.Text = "已是最新版本";
                return;
            }

            UpdateStatusText.Text = $"发现新版本 {version}，正在下载…";
            await AppUpdater.DownloadAsync(beta: false);
            UpdateStatusText.Text = $"新版本 {version} 已就绪";

            var choice = MessageBox.Show(
                $"新版本 {version} 已下载完成，立即重启以完成更新？",
                "RocoPilot", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                AppUpdater.RestartToApply();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败：{ex.GetBaseException().Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async Task PortableCheckUpdateAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RocoPilot");
        using var resp = await http.GetAsync(LatestReleaseUrl);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            UpdateStatusText.Text = "暂无稳定版本";
            return;
        }

        resp.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var pageUrl = doc.RootElement.GetProperty("html_url").GetString();

        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var remote))
        {
            UpdateStatusText.Text = $"无法识别发布版本号：{tag}";
            return;
        }

        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (current is not null && remote > current)
        {
            UpdateStatusText.Text = $"发现新版本 {remote}，已在浏览器打开下载页";
            if (pageUrl is not null)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pageUrl) { UseShellExecute = true });
            }
        }
        else
        {
            UpdateStatusText.Text = "已是最新版本";
        }
    }

    private async void OnDownloadTestClick(object sender, RoutedEventArgs e)
    {
        TestVersionButton.IsEnabled = false;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "正在查询测试版…";

        try
        {
            if (!AppUpdater.IsInstalled)
            {
                await PortableDownloadTestAsync();
                return;
            }

            var version = await AppUpdater.CheckAsync(beta: true);
            if (version is null)
            {
                UpdateStatusText.Text = "暂无测试版发布";
                return;
            }

            UpdateStatusText.Text = $"发现测试版 {version}，正在下载…";
            await AppUpdater.DownloadAsync(beta: true);
            UpdateStatusText.Text = $"测试版 {version} 已就绪";

            var choice = MessageBox.Show(
                $"测试版 {version} 已下载完成，立即重启以完成更新？",
                "RocoPilot", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                AppUpdater.RestartToApply();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"查询失败：{ex.GetBaseException().Message}";
        }
        finally
        {
            TestVersionButton.IsEnabled = true;
        }
    }

    private async Task PortableDownloadTestAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RocoPilot");
        using var resp = await http.GetAsync(ReleasesUrl);
        resp.EnsureSuccessStatusCode();

        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            UpdateStatusText.Text = "暂无测试版发布";
            return;
        }

        var latest = doc.RootElement[0];
        var tag = latest.GetProperty("tag_name").GetString() ?? "";
        var pageUrl = latest.GetProperty("html_url").GetString();
        var pre = latest.GetProperty("prerelease").GetBoolean();

        UpdateStatusText.Text = $"已打开最新{(pre ? "测试" : "稳定")}版 {tag} 下载页";
        if (pageUrl is not null)
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(pageUrl) { UseShellExecute = true });
        }
    }

    public SettingsPage(ISettingsStore store, CaptureHost capture)
    {
        InitializeComponent();

        VersionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        _store = store;
        _capture = capture;

        var shell = store.GetShellSettings();

        _suppressThemeEvent = true;
        ThemeCombo.SelectedIndex = (int)shell.Theme;
        _suppressThemeEvent = false;

        DebugOverlayToggle.IsChecked = shell.DebugOverlay;
        SensitivityPpcXBox.Value = shell.SensitivityPpcX;
        SensitivityPpcYBox.Value = shell.SensitivityPpcY;
    }

    private void OnDebugOverlayChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.DebugOverlay = DebugOverlayToggle.IsChecked == true;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private void OnSensitivityChanged(object sender, RoutedEventArgs e)
    {
        var shell = _store.GetShellSettings();
        shell.SensitivityPpcX = SensitivityPpcXBox.Value ?? 0;
        shell.SensitivityPpcY = SensitivityPpcYBox.Value ?? 0;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        var source = _capture.CurrentSource;
        if (source is null)
        {
            CalibrateHint.Text = "请先开启截图（实时页 → 截图开关）";
            return;
        }

        CalibrateButton.IsEnabled = false;
        CalibrateHint.Text = "校准中…请保持游戏窗口聚焦，勿动鼠标";

        RocoPilot.Capture.WindowFinder.ActivateGameWindow();

        try
        {
            var ppc = await Task.Run(() =>
            {
                using var driver = new InterceptionDriver();
                driver.Arm();
                return AutoCalibrator.Calibrate(source, driver);
            });

            if (ppc is { } result)
            {
                var shell = _store.GetShellSettings();
                shell.SensitivityPpcX = Math.Round(result.PpcX, 3);
                shell.SensitivityPpcY = Math.Round(result.PpcY, 3);
                _store.SetShellSettings(shell);
                _store.Save();
                SensitivityPpcXBox.Value = shell.SensitivityPpcX;
                SensitivityPpcYBox.Value = shell.SensitivityPpcY;
                CalibrateHint.Text = $"校准完成：X={result.PpcX:F3} Y={result.PpcY:F3}（已保存）";
            }
            else
            {
                CalibrateHint.Text = "校准失败：未检测到足够的场景位移（场景纹理不足或镜头未转）";
            }
        }
        catch (Exception ex)
        {
            CalibrateHint.Text = $"校准异常：{ex.GetBaseException().Message}";
        }
        finally
        {
            CalibrateButton.IsEnabled = true;
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeEvent || ThemeCombo.SelectedIndex < 0)
        {
            return;
        }

        ShellTheme.ApplyAndPersist(_store, (AppTheme)ThemeCombo.SelectedIndex, Window.GetWindow(this));
    }
}
