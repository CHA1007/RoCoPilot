using System.IO;
using System.Windows;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Input;
using RocoPilot.Routing;
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

        LineView.EditRequested += OnNodeEditRequested;
        LineView.DeleteRequested += OnNodeDeleteRequested;
        LineView.MoveRequested += OnNodeMoveRequested;
        LineView.RunRequested += OnRunRequested;
        LineView.LoopConfigureRequested += OnLoopConfigureRequested;
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
            SetStatus($"执行图加载失败：{ex.Message}");
        }

        RefreshList();
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

    private void OnLoopConfigureRequested()
    {
        if (!EnsureEditable()) return;

        var owner = Window.GetWindow(this);
        var config = RouteNodeConfigDialog.LoopSettings(owner, _loopsToHead, _maxLaps, _maxDuration);
        if (config is null) return;

        _loopsToHead = config.Enabled;
        _maxLaps = config.MaxLaps;
        _maxDuration = config.MaxDuration;
        SaveAndRefresh();
    }

    private async void OnNodeEditRequested(RouteNode node)
    {
        if (!EnsureEditable()) return;

        var owner = Window.GetWindow(this);
        switch (node.Kind)
        {
            case RouteNodeKind.Anchor:
            {
                var anchorName = RouteNodeConfigDialog.AnchorName(owner, node.AnchorName);
                if (anchorName is null || anchorName == node.AnchorName) return;

                ReplaceNode(new RouteNode(node.Kind, $"锚点·{anchorName}", anchorName: anchorName, id: node.Id));
                SetStatus($"锚点已配置为「{anchorName}」");
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
                    ReplaceNode(new RouteNode(node.Kind, $"回放·{routeName}", routeName: routeName, id: node.Id));
                    SetStatus($"回放步骤已关联路线「{routeName}」");
                }

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

        ReplaceNode(new RouteNode(node.Kind, $"回放·{recorded}", routeName: recorded, id: node.Id));
        SetStatus($"新路线「{recorded}」已关联到步骤「{node.Name}」");
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
        LineView.SetLoop(_loopsToHead, _maxLaps, _maxDuration);
        LineView.SetRunning(_dispatcher.RoutePlaybackEnabled);
        LineView.SetBusy(_dispatcher.RoutePlaybackEnabled || _recorder is not null);
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
                SetStatus($"回放未运行：{toolEvent.Data?["error"]}");
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
        LineView.SetRunning(_dispatcher.RoutePlaybackEnabled);
        LineView.SetBusy(_dispatcher.RoutePlaybackEnabled || _recorder is not null);
    }

    private bool EnsureEditable()
    {
        if (_dispatcher.RoutePlaybackEnabled)
        {
            SetStatus("执行中——先停止再编辑步骤");
            return false;
        }

        if (_recorder is not null)
        {
            SetStatus("录制中——按 X 结束录制后再编辑步骤");
            return false;
        }

        return true;
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
