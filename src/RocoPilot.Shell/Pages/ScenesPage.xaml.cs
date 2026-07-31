using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using RocoPilot.Settings;

namespace RocoPilot.Shell.Pages;

public partial class ScenesPage : Page
{
    private static readonly JsonSerializerOptions SidecarJsonOptions = new() { WriteIndented = true };

    private SceneEntry? _selected;
    private bool _showOverlay = true;

    public ScenesPage()
    {
        InitializeComponent();
        RootPathText.Text = $"留存根：{RocoPaths.LogsRoot}（会话日志滚动留最近 {LogRetention.DefaultKeepSessions}）";
        Loaded += (_, _) => Reload();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        var entries = ScanScenes();
        SceneList.Items.Clear();
        foreach (var entry in entries)
        {
            SceneList.Items.Add(entry);
        }

        SceneCountText.Text = $"现场 {entries.Count} 个（会话 × 现场，新 → 旧）";
        if (entries.Count > 0)
        {
            SceneList.SelectedIndex = 0;
        }
        else
        {
            _selected = null;
            SceneTitleText.Text = "尚未留存现场";
            SceneImage.Source = null;
            SidecarText.Text = "触发条件命中（投空 / 跑丢 / 标定失败 / 识别突变）时，任务管线自动留存现场包。";
        }
    }

    private static List<SceneEntry> ScanScenes()
    {
        var entries = new List<SceneEntry>();
        if (!Directory.Exists(RocoPaths.LogsRoot))
        {
            return entries;
        }

        foreach (var sessionDir in Directory.GetDirectories(RocoPaths.LogsRoot)
                     .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var scenesRoot = Path.Combine(sessionDir, "scenes");
            if (!Directory.Exists(scenesRoot))
            {
                continue;
            }

            var session = Path.GetFileName(sessionDir);
            foreach (var sceneDir in Directory.GetDirectories(scenesRoot)
                         .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                var name = Path.GetFileName(sceneDir);
                entries.Add(new SceneEntry
                {
                    Label = $"{session} · {TriggerLabel(TriggerOf(name))} · {name}",
                    Directory = sceneDir,
                });
            }
        }

        return entries;
    }

    private void OnSceneSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SceneList.SelectedItem is not SceneEntry entry)
        {
            return;
        }

        _selected = entry;
        var name = Path.GetFileName(entry.Directory);
        SceneTitleText.Text = $"{TriggerLabel(TriggerOf(name))} · {name}";
        RefreshImage();
        RefreshSidecar();
    }

    private void OnOverlayChecked(object sender, RoutedEventArgs e)
    {
        _showOverlay = true;
        RefreshImage();
    }

    private void OnKeyframeChecked(object sender, RoutedEventArgs e)
    {
        _showOverlay = false;
        RefreshImage();
    }

    private void RefreshImage()
    {
        if (_selected is null)
        {
            return;
        }

        var preferred = Path.Combine(_selected.Directory, _showOverlay ? "overlay.png" : "keyframe.png");
        var fallback = Path.Combine(_selected.Directory, _showOverlay ? "keyframe.png" : "overlay.png");
        var path = File.Exists(preferred) ? preferred : fallback;
        try
        {
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                SceneImage.Source = bitmap;
            }
            else
            {
                SceneImage.Source = null;
            }
        }
        catch
        {
            SceneImage.Source = null;
        }
    }

    private void RefreshSidecar()
    {
        if (_selected is null)
        {
            return;
        }

        var sidecarPath = Path.Combine(_selected.Directory, "scene.json");
        try
        {
            if (!File.Exists(sidecarPath))
            {
                SidecarText.Text = "（scene.json 缺）";
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            SidecarText.Text = JsonSerializer.Serialize(document, SidecarJsonOptions);
        }
        catch
        {
            SidecarText.Text = "（scene.json 读取失败）";
        }
    }

    private static string TriggerOf(string sceneDirName)
    {
        var index = sceneDirName.IndexOf('-');
        return index >= 0 && index < sceneDirName.Length - 1 ? sceneDirName[(index + 1)..] : "scene";
    }

    private static string TriggerLabel(string trigger) => trigger switch
    {
        "miss" => "投空",
        "fled" => "跑丢",
        "calibration" => "标定失败",
        "jump" => "识别突变",
        _ => trigger,
    };

    private sealed class SceneEntry
    {
        public required string Label { get; init; }

        public required string Directory { get; init; }
    }
}
