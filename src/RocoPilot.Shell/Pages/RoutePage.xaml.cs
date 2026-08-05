using System.IO;
using RocoPilot.Core;
using System.Windows;
using RocoPilot.Capture;
using RocoPilot.Input;
using RocoPilot.Routing;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class RoutePage : System.Windows.Controls.Page
{
    private const string PoiTemplateRoot = "assets/templates/map/poi";
    private const string GraphName = "采集路线图";
    private const ushort EndRecordingScanCode = 0x2D;
    private const ushort KeyUpStateFlag = 0x001;

    private readonly RouteStore _store;
    private readonly CaptureHost _capture;
    private readonly DispatcherHost _dispatcher;

    private List<RouteNode> _nodes = [];
    private List<RouteEdge> _edges = [];

    private Guid? _activeNodeId;
    private int _laps;
    private int _retries;

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

        GraphCanvas.EditRequested += OnNodeEditRequested;
        GraphCanvas.DeleteRequested += OnNodeDeleteRequested;
        GraphCanvas.EdgeRequested += OnEdgeRequested;
        GraphCanvas.EdgeDeleted += OnEdgeDeleted;
        GraphCanvas.NodeMoved += OnNodeMoved;

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
            _edges = [.. graph.Edges];
        }
        catch (FileNotFoundException)
        {
        }
        catch (InvalidDataException ex)
        {
            SetStatus($"执行图加载失败：{ex.Message}");
        }

        GraphCanvas.SetGraph(_nodes, _edges);
    }

    private void OnAddAnchorClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;

        var pois = ListPoiTemplates();
        if (pois.Count == 0)
        {
            SetStatus($"无法添加锚点：{PoiTemplateRoot} 下没有 POI 模板（需玩家真机裁切提供）");
            return;
        }

        AddNode(new RouteNode(RouteNodeKind.Anchor, $"锚点·{pois[0]}", NextX(), NextY(), poiName: pois[0]));
    }

    private async void OnAddPlaybackClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;

        var routes = await _store.ListAsync();
        if (routes.Count == 0)
        {
            SetStatus("尚无已录制的路线——先录制一条");
            var recorded = await StartRecordingAsync();
            if (recorded is null) return;
            AddNode(new RouteNode(RouteNodeKind.Playback, $"回放·{recorded}", NextX(), NextY(), routeName: recorded));
            return;
        }

        AddNode(new RouteNode(RouteNodeKind.Playback, $"回放·{routes[0].Name}", NextX(), NextY(), routeName: routes[0].Name));
    }

    private void OnAddLoopClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        AddNode(new RouteNode(RouteNodeKind.Loop, "循环", NextX(), NextY()));
    }

    private void AddNode(RouteNode node)
    {
        _nodes.Add(node);
        SaveAndRefresh();
        SetStatus($"已添加节点「{node.Name}」——拖到合适位置，双击配置参数");
    }

    private void OnNodeDeleteRequested(Guid nodeId)
    {
        if (!EnsureEditable()) return;

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        _nodes.Remove(node);
        _edges.RemoveAll(edge => edge.FromId == nodeId || edge.ToId == nodeId);
        SaveAndRefresh();
        SetStatus($"已删除节点「{node.Name}」及其连线");
    }

    private void OnEdgeDeleted(RouteEdge edge)
    {
        if (!EnsureEditable()) return;
        if (_edges.RemoveAll(e => e.FromId == edge.FromId && e.ToId == edge.ToId) > 0)
        {
            SaveAndRefresh();
            SetStatus("已删除连线");
        }
    }

    private void OnEdgeRequested(Guid fromId, Guid toId)
    {
        if (!EnsureEditable()) return;

        var from = _nodes.FirstOrDefault(n => n.Id == fromId);
        var to = _nodes.FirstOrDefault(n => n.Id == toId);
        if (from is null || to is null || fromId == toId) return;

        if (_edges.Any(edge => edge.FromId == fromId))
        {
            SetStatus($"连线被拒绝：「{from.Name}」已有出边——v1 仅支持线性链（每节点最多一进一出）");
            return;
        }

        if (_edges.Any(edge => edge.ToId == toId))
        {
            SetStatus($"连线被拒绝：「{to.Name}」已有入边——v1 仅支持线性链（每节点最多一进一出）");
            return;
        }

        if (CreatesCycle(fromId, toId))
        {
            SetStatus("连线被拒绝：会形成环——循环语义请用循环节点表达");
            return;
        }

        _edges.Add(new RouteEdge(fromId, toId));
        SaveAndRefresh();
        SetStatus($"已连接「{from.Name}」→「{to.Name}」");
    }

    private bool CreatesCycle(Guid fromId, Guid toId)
    {
        var next = _edges.ToDictionary(edge => edge.FromId, edge => edge.ToId);
        var current = toId;
        while (next.TryGetValue(current, out var nextId))
        {
            if (nextId == fromId) return true;
            current = nextId;
        }

        return false;
    }

    private void OnNodeMoved(Guid nodeId, double x, double y)
    {
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        _nodes.Remove(node);
        _nodes.Add(Reposition(node, x, y));
        _ = SaveGraphAsync();
    }

    private async void OnNodeEditRequested(RouteNode node)
    {
        if (!EnsureEditable()) return;

        var owner = Window.GetWindow(this);
        switch (node.Kind)
        {
            case RouteNodeKind.Anchor:
            {
                var pois = ListPoiTemplates();
                if (pois.Count == 0)
                {
                    SetStatus($"没有可选 POI：{PoiTemplateRoot} 下无模板");
                    return;
                }

                var poi = RouteNodeConfigDialog.Anchor(owner, pois, node.PoiName);
                if (poi is null || poi == node.PoiName) return;

                ReplaceNode(new RouteNode(
                    node.Kind, $"锚点·{poi}", node.CanvasX, node.CanvasY,
                    poiName: poi, id: node.Id));
                SetStatus($"锚点「{node.Name}」已改为 POI「{poi}」");
                break;
            }

            case RouteNodeKind.Playback:
            {
                var routes = await _store.ListAsync();
                var choice = RouteNodeConfigDialog.Playback(owner, routes, node.RouteName);
                if (choice is null) return;

                if (choice.RecordNew)
                {
                    _ = RecordForNodeAsync(node.Id);
                    return;
                }

                if (choice.RouteName is { } routeName && routeName != node.RouteName)
                {
                    ReplaceNode(new RouteNode(
                        node.Kind, $"回放·{routeName}", node.CanvasX, node.CanvasY,
                        routeName: routeName, id: node.Id));
                    SetStatus($"回放节点已关联路线「{routeName}」");
                }

                break;
            }

            case RouteNodeKind.Loop:
            {
                var config = RouteNodeConfigDialog.Loop(owner, node.MaxLaps, node.MaxDuration);
                if (config is null) return;

                ReplaceNode(new RouteNode(
                    node.Kind, node.Name, node.CanvasX, node.CanvasY,
                    maxLaps: config.MaxLaps, maxDuration: config.MaxDuration, id: node.Id));
                SetStatus("循环节点参数已更新");
                break;
            }
        }
    }

    private async Task RecordForNodeAsync(Guid nodeId)
    {
        var recorded = await StartRecordingAsync();
        if (recorded is null) return;

        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return;

        ReplaceNode(new RouteNode(
            node.Kind, $"回放·{recorded}", node.CanvasX, node.CanvasY,
            routeName: recorded, id: node.Id));
        SetStatus($"新路线「{recorded}」已关联到节点「{node.Name}」");
    }

    private void ReplaceNode(RouteNode replacement)
    {
        var index = _nodes.FindIndex(n => n.Id == replacement.Id);
        if (index < 0) return;
        _nodes[index] = replacement;
        SaveAndRefresh();
    }

    private static RouteNode Reposition(RouteNode node, double x, double y) => new(
        node.Kind,
        node.Name,
        x,
        y,
        node.PoiName,
        node.RouteName,
        node.MaxLaps,
        node.MaxDuration,
        node.Id);

    private RouteGraph BuildGraph() => new(GraphName, _nodes, _edges);

    private void SaveAndRefresh()
    {
        GraphCanvas.SetGraph(_nodes, _edges, _activeNodeId);
        _ = SaveGraphAsync();
    }

    private async Task SaveGraphAsync()
    {
        try
        {
            await _store.SaveGraphAsync(BuildGraph());
        }
        catch (Exception ex)
        {
            SetStatus($"执行图保存失败：{ex.Message}");
        }
    }

    private void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_recorder is not null)
        {
            SetStatus("录制中——按 X 结束录制后再运行");
            return;
        }

        if (!_capture.IsRunning)
        {
            SetStatus("请先开启截图源（启动页）——回放由调度器在截图器运行时驱动");
            return;
        }

        _laps = 0;
        _retries = 0;
        _dispatcher.RoutePlaybackEnabled = true;
        _dispatcher.SyncEnables();
        UpdateToolbarState();
        UpdateStatusLine("已开启路线回放（开放世界场景生效，与自动丢球互斥）");
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _dispatcher.RoutePlaybackEnabled = false;
        _dispatcher.SyncEnables();
        UpdateToolbarState();
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
                    GraphCanvas.SetActive(nodeId);
                }

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
                SetStatus($"回放未运行：{toolEvent.Data?["error"]}");
                break;
        }
    });

    private void ClearActiveNode()
    {
        _activeNodeId = null;
        GraphCanvas.SetActive(null);
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
            SetStatus("已有录制或执行在进行");
            return null;
        }

        var source = _capture.CurrentSource;
        if (source is null)
        {
            SetStatus("请先开启截图源（录制时要同步抓取小地图关键帧）");
            return null;
        }

        var name = $"路线-{DateTime.Now:yyyyMMdd-HHmmss}";
        var driver = InputDriverFactory.Create();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var recorder = new RouteRecorder(driver, source, _store);
            recorder.StrokeObserved += OnRecordedStroke;
            recorder.Start(name, TimeSpan.FromSeconds(10));

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
            SetStatus($"录制启动失败：{ex.Message}");
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
            SetStatus($"录制结束失败：{ex.Message}");
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
        var recording = _recorder is not null;
        var busy = running || recording;

        AddAnchorButton.IsEnabled = !busy;
        AddPlaybackButton.IsEnabled = !busy;
        AddLoopButton.IsEnabled = !busy;
        RunButton.IsEnabled = !busy;
        StopButton.IsEnabled = running;
    }

    private bool EnsureEditable()
    {
        if (_dispatcher.RoutePlaybackEnabled)
        {
            SetStatus("执行中——先停止再编辑图");
            return false;
        }

        if (_recorder is not null)
        {
            SetStatus("录制中——按 X 结束录制后再编辑图");
            return false;
        }

        return true;
    }

    private double NextX() => 32 + (_nodes.Count % 4) * 210;

    private double NextY() => 32 + (_nodes.Count / 4) * 110;

    private static IReadOnlyList<string> ListPoiTemplates()
    {
        if (!Directory.Exists(PoiTemplateRoot)) return [];

        return Directory.EnumerateFiles(PoiTemplateRoot, "*.png")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
