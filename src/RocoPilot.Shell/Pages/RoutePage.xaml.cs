using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UiButton = Wpf.Ui.Controls.Button;
using UiIcon = Wpf.Ui.Controls.SymbolIcon;
using CtlApp = Wpf.Ui.Controls.ControlAppearance;
using Sim = Wpf.Ui.Controls.SymbolRegular;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Input;
using RocoPilot.Routing;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class RoutePage : System.Windows.Controls.Page
{
    private const string GraphName = "采集路线图";
    private const ushort EndRecordingScanCode = 0x2D;
    private const ushort KeyUpStateFlag = 0x001;

    private readonly RouteStore _store;
    private readonly CaptureHost _capture;
    private readonly DispatcherHost _dispatcher;

    private List<RouteNode> _nodes = [];

    private Guid? _activeNodeId;
    private Guid? _selectedNodeId;
    private bool _showGlobalInspector;
    private int _laps;
    private int _retries;
    private bool _loopsToHead;
    private int? _maxLaps;
    private TimeSpan? _maxDuration;

    private bool _graphLoaded;
    private RouteRecorder? _recorder;
    private IInputDriver? _recordDriver;
    private TaskCompletionSource<string?>? _recordCompletion;
    private volatile bool _recordStopRequested;

    public RoutePage(RouteStore store, CaptureHost capture, DispatcherHost dispatcher)
    {
        InitializeComponent();
        _store = store;
        _capture = capture;
        _dispatcher = dispatcher;

        Loaded += OnLoaded;

        LineView.SelectRequested += OnNodeSelectRequested;
        LineView.DeleteRequested += OnNodeDeleteRequested;
        LineView.MoveRequested += OnNodeMoveRequested;
        LineView.RunRequested += OnRunRequested;
        LineView.AddRequested += OnAddRequested;

        _dispatcher.EventRaised += OnDispatcherEvent;
        _dispatcher.Changed += OnDispatcherChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateToolbarState();
        if (_graphLoaded) return;
        _graphLoaded = true;
        try
        {
            var graph = await _store.LoadGraphAsync();
            _nodes = [.. graph.Nodes];
            _loopsToHead = graph.LoopsToHead;
            _maxLaps = graph.MaxLaps;
            _maxDuration = graph.MaxDuration;
        }
        catch (FileNotFoundException)
        {
        }
        catch (InvalidDataException ex)
        {
            if (!ex.Message.Contains("执行链为空", StringComparison.OrdinalIgnoreCase))
                SetError($"路线加载失败：{ex.Message}");
        }

        RefreshList();
        RebuildInspector();
    }

    private void OnAddRequested(RouteNodeKind kind)
    {
        if (!EnsureEditable()) return;
        AddNode(new RouteNode(kind, kind == RouteNodeKind.Anchor ? "锚点" : "回放"));
    }

    private void AddNode(RouteNode node)
    {
        _nodes.Add(node);
        SaveAndRefresh();
    }

    private void OnNodeDeleteRequested(Guid nodeId)
    {
        if (!EnsureEditable()) return;

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        _nodes.Remove(node);
        SaveAndRefresh();
    }

    private void OnNodeMoveRequested(Guid nodeId, int newIndex)
    {
        if (!EnsureEditable())
        {
            RefreshList();
            return;
        }

        var currentIndex = _nodes.FindIndex(n => n.Id == nodeId);
        if (currentIndex < 0) return;

        newIndex = Math.Clamp(newIndex, 0, _nodes.Count - 1);
        if (newIndex == currentIndex)
        {
            RefreshList();
            return;
        }

        var node = _nodes[currentIndex];
        _nodes.RemoveAt(currentIndex);
        _nodes.Insert(newIndex, node);
        SaveAndRefresh();
    }

    private void OnLoopClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        _selectedNodeId = null;
        _showGlobalInspector = true;
        LineView.SetSelected(null);
        RebuildInspector();
    }

    private void OnRunAllClick(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count == 0)
        {
            SetStatus("还没有步骤——先添加锚点或回放");
            return;
        }

        OnRunRequested(_nodes[0].Id);
    }

    private void OnNodeSelectRequested(Guid nodeId)
    {
        if (!EnsureEditable())
        {
            RefreshList();
            return;
        }

        _selectedNodeId = nodeId;
        _showGlobalInspector = false;
        LineView.SetSelected(nodeId);
        RebuildInspector();
    }

    private void RebuildInspector()
    {
        InspectorHost.Children.Clear();
        var node = _selectedNodeId is { } id ? _nodes.FirstOrDefault(n => n.Id == id) : null;
        if (node is null)
        {
            if (_showGlobalInspector)
                AddToInspector(BuildGlobalInspector());
            else if (_nodes.Count == 0)
                AddToInspector(BuildEmptyInspector());
            else
                AddToInspector(BuildPlaceholderInspector());
            return;
        }

        _showGlobalInspector = false;
        switch (node.Kind)
        {
            case RouteNodeKind.Anchor:
                AddToInspector(BuildAnchorInspector(node));
                break;
            case RouteNodeKind.Playback:
                AddToInspector(BuildPlaybackInspector(node));
                break;
        }
    }

    private void AddToInspector(FrameworkElement content) => InspectorHost.Children.Add(content);

    private static FrameworkElement BuildEmptyInspector()
    {
        var icon = new UiIcon
        {
            Symbol = Sim.Info24,
            FontSize = 24,
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFillColorTertiaryBrush");

        var text = new TextBlock
        {
            Text = "配置步骤后在此查看详情",
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        text.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(text);

        var host = new Grid { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Children.Add(content);
        return host;
    }

    private static FrameworkElement BuildPlaceholderInspector()
    {
        var icon = new UiIcon
        {
            Symbol = Sim.Info24,
            FontSize = 28,
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFillColorTertiaryBrush");

        var text = new TextBlock
        {
            Text = "选择左侧步骤查看详情",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        text.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(text);

        var host = new Grid { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Children.Add(content);
        return host;
    }

    private StackPanel BuildGlobalInspector()
    {
        var s = new StackPanel { Margin = new Thickness(16) };
        s.Children.Add(InspectorTitle("整条路线"));

        var enable = new CheckBox { Content = "循环回到开头", IsChecked = _loopsToHead, Margin = new Thickness(0, 14, 0, 0) };
        var laps = new TextBox { Text = _maxLaps?.ToString() ?? string.Empty, Margin = new Thickness(0, 6, 0, 0) };
        var minutes = new TextBox
        {
            Text = _maxDuration is { } d ? d.TotalMinutes.ToString("0.#") : string.Empty,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var apply = GhostButton("应用到路线", Sim.Checkmark24);
        apply.Margin = new Thickness(0, 20, 0, 0);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Click += (_, _) => ApplyGlobalSettings(enable, laps, minutes);

        s.Children.Add(InspectorLabel("圈数上限"));
        s.Children.Add(laps);
        s.Children.Add(InspectorLabel("时长上限（分钟）"));
        s.Children.Add(minutes);
        s.Children.Add(enable);
        s.Children.Add(apply);
        return s;
    }

    private void ApplyGlobalSettings(CheckBox enable, TextBox lapsBox, TextBox minutesBox)
    {
        if (!TryParseLaps(lapsBox.Text, out var laps, out var lapsError))
        {
            SetError(lapsError);
            return;
        }

        if (!TryParseMinutes(minutesBox.Text, out var minutes, out var minutesError))
        {
            SetError(minutesError);
            return;
        }

        if (laps is { } l && l < 1)
        {
            SetError("圈数上限至少为 1。");
            return;
        }

        if (minutes is { } m && m <= 0)
        {
            SetError("时长上限必须为正。");
            return;
        }

        _loopsToHead = enable.IsChecked == true;
        _maxLaps = laps;
        _maxDuration = minutes is { } value ? TimeSpan.FromMinutes(value) : null;
        SaveAndRefresh();
        RebuildInspector();
    }

    private StackPanel BuildAnchorInspector(RouteNode node)
    {
        var s = new StackPanel { Margin = new Thickness(16) };
        s.Children.Add(InspectorTitle("魔力之源"));

        var combo = new ComboBox
        {
            IsEditable = true,
            Text = node.AnchorName ?? string.Empty,
            Margin = new Thickness(0, 14, 0, 0),
        };
        foreach (var entry in AnchorCatalog.GroundEntries) combo.Items.Add(entry.Name);
        combo.SelectionChanged += (_, _) => ApplyAnchor(node, combo);

        s.Children.Add(combo);
        s.Children.Add(InspectorDanger(node));
        return s;
    }

    private void ApplyAnchor(RouteNode node, ComboBox combo)
    {
        var name = (combo.SelectedItem as string) ?? combo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == node.AnchorName) return;
        if (AnchorCatalog.GroundEntries.All(entry => entry.Name != name))
        {
            SetError($"「{name}」不在内置魔力之源目录中——请从下拉列表选择官方名称。");
            return;
        }

        ReplaceNode(new RouteNode(node.Kind, $"锚点·{name}", anchorName: name, id: node.Id));
    }

    private StackPanel BuildPlaybackInspector(RouteNode node)
    {
        var s = new StackPanel { Margin = new Thickness(16) };
        s.Children.Add(InspectorTitle("关联路线"));

        var combo = new ComboBox { Margin = new Thickness(0, 14, 0, 0) };
        var record = GhostButton("录制新路线…", Sim.Record24);
        record.Margin = new Thickness(0, 14, 0, 0);
        record.HorizontalAlignment = HorizontalAlignment.Left;
        record.Click += (_, _) => _ = RecordForNodeAsync(node.Id);

        s.Children.Add(combo);
        s.Children.Add(record);
        s.Children.Add(InspectorDanger(node));

        _ = PopulateRoutesAsync(combo, node);
        return s;
    }

    private async Task PopulateRoutesAsync(ComboBox combo, RouteNode node)
    {
        try
        {
            var routes = await _store.ListAsync();
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
                if ((string?)item.Tag == node.RouteName)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }

            if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) => ApplyPlayback(node, combo);
        }
        catch (Exception ex)
        {
            SetStatus($"路线列表加载失败：{ex.Message}");
        }
    }

    private void ApplyPlayback(RouteNode node, ComboBox combo)
    {
        var routeName = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (routeName is null || routeName == node.RouteName) return;

        ReplaceNode(new RouteNode(node.Kind, $"回放·{routeName}", routeName: routeName, id: node.Id));
    }

    private static UiButton GhostButton(string content, Sim symbol)
    {
        return new UiButton
        {
            Content = content,
            Icon = new UiIcon { Symbol = symbol, FontSize = 14, Width = 14, Height = 14 },
            Appearance = CtlApp.Secondary,
            Background = Brushes.Transparent,
            Foreground = RouteVisuals.AccentBrush,
            BorderBrush = RouteVisuals.AccentBrush,
            BorderThickness = new Thickness(1),
            MouseOverBackground = RouteVisuals.AccentGhostHoverBrush,
            PressedBackground = RouteVisuals.AccentGhostPressedBrush,
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5),
        };
    }

    private static TextBlock InspectorTitle(string title)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
        };
        heading.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        return heading;
    }

    private static TextBlock InspectorLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
        };
        label.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        return label;
    }

    private UiButton InspectorDanger(RouteNode node)
    {
        var button = new UiButton
        {
            Content = "删除此步骤",
            Appearance = CtlApp.Danger,
            Icon = new UiIcon { Symbol = Sim.Dismiss24, FontSize = 14, Width = 14, Height = 14 },
            Margin = new Thickness(0, 22, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            _nodes.Remove(node);
            _selectedNodeId = null;
            SaveAndRefresh();
            RebuildInspector();
        };
        return button;
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

    private async Task RecordForNodeAsync(Guid nodeId)
    {
        var recorded = await StartRecordingAsync();
        if (recorded is null) return;

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        ReplaceNode(new RouteNode(node.Kind, $"回放·{recorded}", routeName: recorded, id: node.Id));
    }

    private void ReplaceNode(RouteNode replacement)
    {
        var index = _nodes.FindIndex(n => n.Id == replacement.Id);
        if (index < 0) return;
        _nodes[index] = replacement;
        SaveAndRefresh();
    }

    private RouteGraph BuildGraph() => new(GraphName, _nodes, _loopsToHead, _maxLaps, _maxDuration);

    private void SaveAndRefresh()
    {
        RefreshList();
        _ = SaveGraphAsync();
    }

    private void RefreshList()
    {
        LineView.SetSteps(_nodes, _activeNodeId);
        LineView.SetSelected(_selectedNodeId);
        LineView.SetLoop(_loopsToHead, _maxLaps, _maxDuration);
        LineView.SetRunning(_dispatcher.RoutePlaybackEnabled);
        LineView.SetBusy(_dispatcher.RoutePlaybackEnabled || _recorder is not null);
        UpdateToolbarState();
    }

    private async Task SaveGraphAsync()
    {
        try
        {
            await _store.SaveGraphAsync(BuildGraph());
        }
        catch (Exception ex)
        {
            SetError($"执行图保存失败：{ex.Message}");
        }
    }

    private void OnRunRequested(Guid nodeId)
    {
        if (_dispatcher.RoutePlaybackEnabled)
        {
            _dispatcher.RoutePlaybackEnabled = false;
            _dispatcher.SyncEnables();
            UpdateToolbarState();
            return;
        }

        if (_recorder is not null)
        {
            SetError("录制中——按 X 结束录制后再运行");
            return;
        }

        if (!_capture.IsRunning)
        {
            SetError("请先开启截图源（启动页）——回放由调度器在截图器运行时驱动");
            return;
        }

        _laps = 0;
        _retries = 0;
        _dispatcher.StartRoutePlayback(nodeId);
        WindowFinder.ActivateGameWindow();
        UpdateToolbarState();
        UpdateStatusLine("已开启路线回放（与自动丢球互斥，地图快传临时停用，停止后恢复）");
    }

    private void OnDispatcherChanged() => Dispatcher.BeginInvoke(() =>
    {
        UpdateToolbarState();
        UpdateStatusLine();
    });

    private void OnDispatcherEvent(object? sender, ToolEvent toolEvent) => Dispatcher.BeginInvoke(() =>
    {
        switch (toolEvent.Name)
        {
            case "node_started":
                if (toolEvent.Data?["node_id"] is string nodeIdText
                    && Guid.TryParse(nodeIdText, out var nodeId))
                {
                    _activeNodeId = nodeId;
                    LineView.SetActive(nodeId);
                }

                if (toolEvent.Data?["kind"] as string == nameof(RouteNodeKind.Anchor))
                    UpdateStatusLine("锚点传送：自动开图并传送中");
                else
                    UpdateStatusLine();
                break;

            case "loop_lap":
                if (toolEvent.Data?["lap"] is int lap) _laps = lap;
                UpdateStatusLine();
                break;

            case "stuck_retry":
                _retries++;
                UpdateStatusLine($"节点重试（{_retries} 次）：{toolEvent.Data?["reason"]}");
                break;

            case "anchor_fallback":
                UpdateStatusLine($"回退到锚点「{toolEvent.Data?["anchor"]}」重跑");
                break;

            case "route_suspended":
                ClearActiveNode();
                SetStatus(_dispatcher.RoutePlaybackEnabled
                    ? "回放挂起（战斗或失焦），恢复后自动续跑"
                    : "已停止路线回放");
                break;

            case "graph_finished":
                ClearActiveNode();
                SetStatus($"运行结束：{toolEvent.Data?["message"]}·失败重试 {_retries} 次");
                _laps = 0;
                _retries = 0;
                UpdateToolbarState();
                break;

            case "route_playback_fault":
                ClearActiveNode();
                SetError($"回放未运行：{toolEvent.Data?["error"]}");
                break;
        }
    });

    private void ClearActiveNode()
    {
        _activeNodeId = null;
        LineView.SetActive(null);
    }

    private void UpdateStatusLine(string? transient = null)
    {
        if (!_dispatcher.RoutePlaybackEnabled)
        {
            if (transient is not null) SetStatus(transient);
            return;
        }

        var current = _nodes.FirstOrDefault(n => n.Id == _activeNodeId)?.Name ?? "—";
        var detail = transient is null ? string.Empty : $"｜{transient}";
        SetStatus($"当前节点：{current}｜已跑圈数：{_laps}｜失败重试：{_retries}{detail}");
    }

    private async Task<string?> StartRecordingAsync()
    {
        if (_recorder is not null || _dispatcher.RoutePlaybackEnabled)
        {
            SetError("已有录制或执行在进行");
            return null;
        }

        var source = _capture.CurrentSource;
        if (source is null)
        {
            SetError("请先开启截图源（录制时要同步抓取小地图关键帧）");
            return null;
        }

        var name = $"路线-{DateTime.Now:yyyyMMdd-HHmmss}";
        var driver = InputDriverFactory.Create();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var recorder = new RouteRecorder(driver, source, _store);
            recorder.StrokeObserved += OnRecordedStroke;
            recorder.Start(name);

            _recorder = recorder;
            _recordDriver = driver;
            _recordCompletion = completion;
            _recordStopRequested = false;
            SetRecordingUi(true);
            SetStatus($"录制「{name}」中——切到游戏真实操作，按 X 结束");
            return await completion.Task;
        }
        catch (Exception ex)
        {
            driver.Dispose();
            SetRecordingUi(false);
            SetError($"录制启动失败：{ex.Message}");
            completion.TrySetResult(null);
            return null;
        }
    }

    private void OnRecordedStroke(ReceivedStroke stroke)
    {
        if (_recordStopRequested) return;
        if (stroke.Kind != ReceivedDeviceKind.Keyboard) return;
        if (stroke.Code != EndRecordingScanCode || (stroke.State & KeyUpStateFlag) != 0) return;

        _recordStopRequested = true;
        Dispatcher.BeginInvoke(EndRecording);
    }

    private async void EndRecording()
    {
        var recorder = _recorder;
        var completion = _recordCompletion;
        if (recorder is null || completion is null) return;

        try
        {
            var route = await recorder.StopAsync();
            CleanupRecording();
            SetStatus($"路线「{route.Name}」已录制：{route.Events.Count} 个事件 · {route.Duration.TotalSeconds:0.#} 秒");
            completion.TrySetResult(route.Name);
        }
        catch (Exception ex)
        {
            CleanupRecording();
            SetError($"录制结束失败：{ex.Message}");
            completion.TrySetResult(null);
        }
    }

    private void CleanupRecording()
    {
        _recorder = null;
        _recordDriver?.Dispose();
        _recordDriver = null;
        _recordCompletion = null;
        SetRecordingUi(false);
    }

    private void SetRecordingUi(bool recording)
    {
        RecordingBanner.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        UpdateToolbarState();
    }

    private void UpdateToolbarState()
    {
        var running = _dispatcher.RoutePlaybackEnabled;
        var busy = running || _recorder is not null;

        LineView.SetRunning(running);
        LineView.SetBusy(busy);

        RunAllButton.Content = running ? "停止" : "运行整条路线";
        RunAllButton.Icon = new UiIcon { Symbol = running ? Sim.Stop24 : Sim.Play24, FontSize = 16, Width = 16, Height = 16 };
        RunAllButton.IsEnabled = _nodes.Count > 0;
        RunAllButton.Opacity = _nodes.Count > 0 ? 1.0 : 0.4;
        LoopButton.IsEnabled = !busy;
        UpdateLoopChip();
    }

    private void UpdateLoopChip()
    {
        LoopButton.Content = _loopsToHead
            ? $"循环：{LoopChipDetail(_maxLaps, _maxDuration)}"
            : "循环：关闭";
    }

    private static string LoopChipDetail(int? maxLaps, TimeSpan? maxDuration)
    {
        if (maxLaps is null && maxDuration is null) return "无限";

        var parts = new List<string>();
        if (maxLaps is { } laps) parts.Add($"≤{laps} 圈");
        if (maxDuration is { } duration) parts.Add($"≤{duration.TotalMinutes:0.#} 分钟");
        return string.Join(" · ", parts);
    }

    private bool EnsureEditable()
    {
        if (_dispatcher.RoutePlaybackEnabled)
        {
            SetError("执行中——先停止再编辑步骤");
            return false;
        }

        if (_recorder is not null)
        {
            SetError("录制中——按 X 结束录制后再编辑步骤");
            return false;
        }

        return true;
    }

    private void SetError(string text)
    {
        ErrorText.Text = text;
        ErrorBanner.Visibility = Visibility.Visible;
        StatusText.Text = string.Empty;
    }

    private void SetStatus(string text)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        StatusText.Text = text;
    }
}
