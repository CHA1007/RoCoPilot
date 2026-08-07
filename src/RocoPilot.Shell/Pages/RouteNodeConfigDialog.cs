using System.Windows;
using System.Windows.Controls;
using RocoPilot.Routing;

namespace RocoPilot.Shell.Pages;

public sealed record PlaybackConfig(string? RouteName, bool RecordNew);

public sealed record LoopSettingsConfig(bool Enabled, int? MaxLaps, TimeSpan? MaxDuration);

internal static class RouteNodeConfigDialog
{
    public static string? AnchorName(Window? owner, string? currentName)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 260,
            Text = currentName ?? string.Empty,
        };
        foreach (var entry in AnchorCatalog.GroundEntries) combo.Items.Add(entry.Name);

        var window = BuildWindow(
            owner,
            "锚点节点配置",
            Hint("选择内置目录中的官方魔力之源名（地面层，共 39 个；地下一层暂不支持）。传送时会自动在地图上对齐定位，无需录入坐标。"),
            combo,
            null,
            out var ok,
            out var cancel);

        string? result = null;
        ok.Click += (_, _) =>
        {
            var name = combo.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(owner, "请选择魔力之源。", "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (AnchorCatalog.GroundEntries.All(entry => entry.Name != name))
            {
                MessageBox.Show(
                    owner,
                    $"「{name}」不在内置魔力之源目录中——请从下拉列表选择官方名称。",
                    "RocoPilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            result = name;
            window.DialogResult = true;
        };
        cancel.Click += (_, _) => window.DialogResult = false;

        return window.ShowDialog() == true ? result : null;
    }

    public static PlaybackConfig? Playback(Window? owner, IReadOnlyList<RouteSummary> routes, string? currentRoute)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0), MinWidth = 260 };
        foreach (var route in routes)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"{route.Name}（{route.Duration:mm\\:ss} · {route.RecordedAt:MM-dd HH:mm}）",
                Tag = route.Name,
            });
        }

        foreach (ComboBoxItem item in combo.Items)
        {
            if ((string?)item.Tag == currentRoute)
            {
                combo.SelectedItem = item;
                break;
            }
        }

        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;

        var recordButton = new Button
        {
            Content = "录制新路线…",
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var window = BuildWindow(
            owner,
            "回放节点配置",
            Hint("选择要回放的已有路线，或录制一条新路线后自动关联到本节点。"),
            combo,
            recordButton,
            out var ok,
            out var cancel);

        ok.IsEnabled = combo.Items.Count > 0;

        PlaybackConfig? result = null;
        ok.Click += (_, _) =>
        {
            result = new PlaybackConfig((combo.SelectedItem as ComboBoxItem)?.Tag as string, RecordNew: false);
            window.DialogResult = true;
        };
        cancel.Click += (_, _) => window.DialogResult = false;
        recordButton.Click += (_, _) =>
        {
            result = new PlaybackConfig(null, RecordNew: true);
            window.DialogResult = true;
        };

        return window.ShowDialog() == true ? result : null;
    }

    public static LoopSettingsConfig? LoopSettings(Window? owner, bool enabled, int? currentLaps, TimeSpan? currentDuration)
    {
        var enableBox = new CheckBox
        {
            Content = "启用整图循环",
            IsChecked = enabled,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var lapsBox = new TextBox
        {
            Text = currentLaps?.ToString() ?? string.Empty,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var minutesBox = new TextBox
        {
            Text = currentDuration is { } duration ? duration.TotalMinutes.ToString("0.#") : string.Empty,
            Margin = new Thickness(0, 4, 0, 12),
        };

        var window = BuildWindowCore(
            owner,
            "循环配置",
            [Hint("开启后步骤列表跑完从头再跑；两个上限留空即为无限循环，任一上限满足即结束。"),
             enableBox,
             Label("圈数上限"),
             lapsBox,
             Label("时长上限（分钟）"),
             minutesBox],
            out var ok,
            out var cancel);

        LoopSettingsConfig? result = null;
        ok.Click += (_, _) =>
        {
            if (!TryParseLaps(lapsBox.Text, out var laps, out var lapsError))
            {
                MessageBox.Show(owner, lapsError, "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseMinutes(minutesBox.Text, out var minutes, out var minutesError))
            {
                MessageBox.Show(owner, minutesError, "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (laps is { } l && l < 1)
            {
                MessageBox.Show(owner, "圈数上限至少为 1。", "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (minutes is { } m && m <= 0)
            {
                MessageBox.Show(owner, "时长上限必须为正。", "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = new LoopSettingsConfig(
                enableBox.IsChecked == true,
                laps,
                minutes is { } value ? TimeSpan.FromMinutes(value) : null);
            window.DialogResult = true;
        };
        cancel.Click += (_, _) => window.DialogResult = false;

        return window.ShowDialog() == true ? result : null;
    }

    private static bool TryParseLaps(string text, out int? laps, out string error)
    {
        laps = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!int.TryParse(text.Trim(), out var parsed))
        {
            error = "圈数必须是整数（留空表示不限）。";
            return false;
        }

        laps = parsed;
        return true;
    }

    private static bool TryParseMinutes(string text, out double? minutes, out string error)
    {
        minutes = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!double.TryParse(text.Trim(), out var parsed))
        {
            error = "时长必须是数字（留空表示不限）。";
            return false;
        }

        minutes = parsed;
        return true;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Opacity = 0.75,
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static Window BuildWindow(
        Window? owner,
        string title,
        FrameworkElement hint,
        FrameworkElement first,
        FrameworkElement? second,
        out Button ok,
        out Button cancel)
    {
        var elements = new List<FrameworkElement> { hint, first };
        if (second is not null) elements.Add(second);
        return BuildWindowCore(owner, title, elements, out ok, out cancel);
    }

    private static Window BuildWindow(
        Window? owner,
        string title,
        FrameworkElement hint,
        FrameworkElement label1,
        FrameworkElement first,
        FrameworkElement label2,
        FrameworkElement second,
        out Button ok,
        out Button cancel)
    {
        return BuildWindowCore(owner, title, [hint, label1, first, label2, second], out ok, out cancel);
    }

    private static Window BuildWindowCore(Window? owner, string title, IReadOnlyList<FrameworkElement> content, out Button ok, out Button cancel)
    {
        ok = new Button { Content = "确定", MinWidth = 76, IsDefault = true };
        cancel = new Button { Content = "取消", MinWidth = 76, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(20) };
        foreach (var element in content) body.Children.Add(element);
        body.Children.Add(buttons);

        return new Window
        {
            Title = title,
            Owner = owner,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body,
        };
    }
}
