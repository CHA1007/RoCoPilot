using System.Windows;
using System.Windows.Controls;
using RocoPilot.Routing;

namespace RocoPilot.Shell.Pages;

public sealed record PlaybackConfig(string? RouteName, bool RecordNew);

public sealed record LoopConfig(int? MaxLaps, TimeSpan? MaxDuration);

internal static class RouteNodeConfigDialog
{
    public static string? Anchor(Window? owner, IReadOnlyList<string> poiNames, string? currentPoi)
    {
        if (poiNames.Count == 0) return null;

        var combo = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var poi in poiNames) combo.Items.Add(poi);
        combo.SelectedItem = poiNames.Contains(currentPoi ?? string.Empty) ? currentPoi : poiNames[0];

        var window = BuildWindow(
            owner,
            "锚点节点配置",
            Hint("选择该锚点对应的魔力之源 POI（按 assets/templates/map/poi 已有模板列表）。"),
            combo,
            null,
            out var ok,
            out var cancel);

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = combo.SelectedItem as string;
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

    public static LoopConfig? Loop(Window? owner, int? currentLaps, TimeSpan? currentDuration)
    {
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

        var window = BuildWindow(
            owner,
            "循环节点配置",
            Hint("两个上限留空即为无限循环；任一上限满足即结束整图。"),
            Label("圈数上限"),
            lapsBox,
            Label("时长上限（分钟）"),
            minutesBox,
            out var ok,
            out var cancel);

        LoopConfig? result = null;
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

            result = new LoopConfig(laps, minutes is { } value ? TimeSpan.FromMinutes(value) : null);
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
