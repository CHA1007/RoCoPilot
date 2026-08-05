using System.Windows;
using System.Windows.Controls;
using RocoPilot.Routing;

namespace RocoPilot.Shell.Pages;

internal static class AnchorRosterDialog
{
    private const double DuplicateDistance = 0.03;

    private sealed class CandidateRow
    {
        public required double X { get; init; }

        public required double Y { get; init; }

        public required ComboBox NameBox { get; init; }
    }

    public static IReadOnlyList<AnchorEntry>? Manage(
        Window? owner,
        IReadOnlyList<AnchorEntry> current,
        Func<CancellationToken, Task<IReadOnlyList<AnchorScanHit>>> scanAsync)
    {
        var entries = new List<AnchorEntry>(current);
        var candidates = new List<CandidateRow>();

        var rosterList = new ListBox
        {
            MaxHeight = 200,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var deleteButton = new Button
        {
            Content = "删除选中",
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var candidatePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var scanButton = new Button { Content = "扫描地图添加…" };
        var ok = new Button { Content = "确定", MinWidth = 76, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 76, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

        var window = new Window
        {
            Title = "魔力之源锚点名单",
            Owner = owner,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        void RefreshRosterList()
        {
            rosterList.Items.Clear();
            foreach (var entry in entries)
                rosterList.Items.Add($"{entry.Name}（{entry.X:P0}, {entry.Y:P0}）");
        }

        void SetBusy(bool busy)
        {
            scanButton.IsEnabled = !busy;
            deleteButton.IsEnabled = !busy;
            ok.IsEnabled = !busy;
        }

        void AddCandidateRow(AnchorScanHit hit)
        {
            var nameBox = new ComboBox
            {
                IsEditable = true,
                Width = 280,
                Margin = new Thickness(0, 0, 8, 0),
            };
            foreach (var name in AvailableCatalogNames(entries, candidates))
                nameBox.Items.Add(name);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
            };
            row.Children.Add(nameBox);
            row.Children.Add(new TextBlock
            {
                Text = $"（{hit.X:P0}, {hit.Y:P0}）",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = 0.75,
            });
            candidatePanel.Children.Add(row);
            candidates.Add(new CandidateRow { X = hit.X, Y = hit.Y, NameBox = nameBox });
        }

        deleteButton.Click += (_, _) =>
        {
            var index = rosterList.SelectedIndex;
            if (index < 0 || index >= entries.Count) return;
            entries.RemoveAt(index);
            RefreshRosterList();
        };

        scanButton.Click += async (_, _) =>
        {
            SetBusy(true);
            status.Text = "扫描中——请保持游戏前台并确认已打开世界地图";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var hits = await scanAsync(timeout.Token);

                var added = 0;
                var skipped = 0;
                foreach (var hit in hits)
                {
                    if (IsDuplicate(hit, entries, candidates))
                    {
                        skipped++;
                        continue;
                    }

                    AddCandidateRow(hit);
                    added++;
                }

                status.Text = $"扫描完成：新增 {added} 个候选"
                    + (skipped > 0 ? $"，忽略 {skipped} 个与已登记锚点过近的匹配" : string.Empty)
                    + "；为每个候选选择名称后确定";
            }
            catch (OperationCanceledException)
            {
                status.Text = "扫描已取消";
            }
            catch (Exception ex)
            {
                status.Text = $"扫描失败：{ex.GetBaseException().Message}";
            }
            finally
            {
                SetBusy(false);
            }
        };

        IReadOnlyList<AnchorEntry>? result = null;
        ok.Click += (_, _) =>
        {
            var named = new List<AnchorEntry>(entries);
            foreach (var candidate in candidates)
            {
                var name = candidate.NameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (named.Any(entry => entry.Name == name))
                {
                    MessageBox.Show(owner, $"锚点名「{name}」重复。", "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                named.Add(new AnchorEntry(name, candidate.X, candidate.Y));
            }

            result = named;
            window.DialogResult = true;
        };
        cancel.Click += (_, _) => window.DialogResult = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock
        {
            Text = "扫描世界地图（校准：家园 → 关闭 → 缩到最小）检测同屏的全部魔力之源，再为每个检测结果选择名称。"
                + "名称清单来自官方地图数据；未选择名称的候选不会保存。地下一层（B1）锚点暂不支持。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
        });
        body.Children.Add(new TextBlock { Text = "已登记锚点", FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        body.Children.Add(rosterList);
        body.Children.Add(deleteButton);
        body.Children.Add(new TextBlock { Text = "本次扫描候选", FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        body.Children.Add(candidatePanel);
        body.Children.Add(scanButton);
        body.Children.Add(status);
        body.Children.Add(buttons);
        window.Content = body;

        RefreshRosterList();
        if (entries.Count == 0)
            status.Text = "名单为空——游戏内打开世界地图后点「扫描地图添加…」";

        return window.ShowDialog() == true ? result : null;
    }

    private static bool IsDuplicate(AnchorScanHit hit, IReadOnlyList<AnchorEntry> entries, IReadOnlyList<CandidateRow> candidates)
    {
        if (entries.Any(entry => Distance(hit.X, hit.Y, entry.X, entry.Y) < DuplicateDistance))
            return true;

        return candidates.Any(candidate => Distance(hit.X, hit.Y, candidate.X, candidate.Y) < DuplicateDistance);
    }

    private static IEnumerable<string> AvailableCatalogNames(IReadOnlyList<AnchorEntry> entries, IReadOnlyList<CandidateRow> candidates)
    {
        var used = entries.Select(entry => entry.Name)
            .Concat(candidates.Select(candidate => candidate.NameBox.Text?.Trim() ?? string.Empty))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet();

        return AnchorCatalog.GroundEntries
            .Select(entry => entry.Name)
            .Where(name => !used.Contains(name));
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
