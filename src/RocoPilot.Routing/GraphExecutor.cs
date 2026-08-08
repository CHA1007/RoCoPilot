using System.Diagnostics;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Scripting;

namespace RocoPilot.Routing;

public enum GraphCompletionReason
{
    Completed,
    Stopped,
    Faulted,
}

public sealed record GraphExecutionResult(
    GraphCompletionReason Reason,
    string Message,
    int LapsCompleted,
    string? FailedNode)
{
    public bool Succeeded => Reason == GraphCompletionReason.Completed;
}

public sealed class GraphExecutorOptions
{
    public int MaxNodeRetries { get; init; } = 2;

    public TimeSpan SegmentPauseMin { get; init; } = TimeSpan.FromMilliseconds(1500);

    public TimeSpan SegmentPauseMax { get; init; } = TimeSpan.FromMilliseconds(4000);
}

internal enum NodeRunResult
{
    Succeeded,
    Failed,
    Canceled,
}

public sealed class GraphExecutor
{
    private readonly PoiTeleportGuide _teleport;
    private readonly IInputDriver? _driver;
    private readonly ScriptStore? _scriptStore;
    private readonly StrokeReplayer _replayer = new();
    private readonly Action<ToolEvent>? _emitEvent;
    private readonly GraphExecutorOptions _options;
    private readonly Random _pauseRandom = new();
    private readonly Stopwatch _runWatch = new();
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private volatile bool _paused;

    private RouteGraph? _graph;
    private IReadOnlyList<ActionNode> _chain = [];
    private bool _loopsToHead;
    private int? _maxLaps;
    private TimeSpan? _maxDuration;
    private bool _singleNode;
    private int _index;
    private int _laps;
    private int _attempts;

    public GraphExecutor(
        PoiTeleportGuide teleport,
        Action<ToolEvent>? emitEvent = null,
        GraphExecutorOptions? options = null,
        IInputDriver? inputDriver = null,
        ScriptStore? scriptStore = null)
    {
        _teleport = teleport ?? throw new ArgumentNullException(nameof(teleport));
        _emitEvent = emitEvent;
        _options = options ?? new GraphExecutorOptions();
        _driver = inputDriver;
        _scriptStore = scriptStore;
    }

    public RouteGraph? CurrentGraph => _graph;

    public void Pause()
    {
        _paused = true;
        _pauseGate.Reset();
    }

    public void Resume()
    {
        _paused = false;
        _pauseGate.Set();
    }

    public void Reset()
    {
        _graph = null;
        _chain = [];
        _loopsToHead = false;
        _maxLaps = null;
        _maxDuration = null;
        _singleNode = false;
        _index = 0;
        _laps = 0;
        _attempts = 0;
        _runWatch.Reset();
        _paused = false;
        _pauseGate.Set();
    }

    public async Task<GraphExecutionResult> RunAsync(
        RouteGraph graph,
        Guid? startNodeId = null,
        bool singleNode = false,
        CancellationToken stoppingToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (!ReferenceEquals(graph, _graph))
        {
            OrderedRouteChain ordered;
            try
            {
                ordered = graph.OrderedChain();
            }
            catch (InvalidOperationException ex)
            {
                return new GraphExecutionResult(GraphCompletionReason.Faulted, ex.Message, 0, null);
            }

            var chain = ordered.Nodes;
            _loopsToHead = ordered.LoopsToHead;
            _maxLaps = graph.MaxLaps;
            _maxDuration = graph.MaxDuration;
            _singleNode = singleNode;

            var startIndex = 0;
            if (startNodeId is { } startId)
            {
                var index = IndexOfNode(chain, startId);
                if (index < 0)
                {
                    var name = graph.Nodes.FirstOrDefault(node => node.Id == startId)?.Name ?? startId.ToString();
                    return new GraphExecutionResult(
                        GraphCompletionReason.Faulted,
                        $"节点「{name}」不在步骤列表中。",
                        0,
                        null);
                }

                startIndex = index;
            }

            _graph = graph;
            _chain = chain;
            _index = startIndex;
            _laps = 0;
            _attempts = 0;
            _runWatch.Restart();

            if (_chain.Count == 0)
            {
                Reset();
                return new GraphExecutionResult(GraphCompletionReason.Faulted, "执行图为空。", 0, null);
            }

            Emit("route_started", new Dictionary<string, object?>
            {
                ["graph"] = graph.Name,
                ["nodes"] = _chain.Count,
            });
        }

        _runWatch.Start();

        while (true)
        {
            if (_paused)
            {
                try
                {
                    _pauseGate.Wait(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _runWatch.Stop();
                    return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                _runWatch.Stop();
                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
            }

            var node = _chain[_index];

            Emit("node_started", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["node_id"] = node.Id.ToString(),
            });

            var runResult = await RunNodeAsync(node, stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                _runWatch.Stop();
                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
            }

            if (runResult == NodeRunResult.Succeeded)
            {
                SegmentDone(node);

                if (_singleNode)
                    return FinishCompleted($"节点「{node.Name}」试跑完成。");

                if (!await SegmentPauseAsync(stoppingToken))
                {
                    _runWatch.Stop();
                    return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                }
                _index++;
                _attempts = 0;
            }
            else if (runResult == NodeRunResult.Failed)
            {
                if (FailNode(node) is { } fault)
                    return FinishWithFault(fault, node);
                if (stoppingToken.IsCancellationRequested)
                {
                    _runWatch.Stop();
                    return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                }
            }

            if (_index >= _chain.Count)
            {
                if (!_loopsToHead)
                    return FinishCompleted("执行链运行完成。");

                _laps++;
                Emit("loop_lap", new Dictionary<string, object?>
                {
                    ["lap"] = _laps,
                    ["elapsed_ms"] = _runWatch.Elapsed.TotalMilliseconds,
                });

                if (_maxLaps is { } maxLaps && _laps >= maxLaps)
                    return FinishCompleted($"达到圈数上限（{maxLaps} 圈）。");
                if (_maxDuration is { } maxDuration && _runWatch.Elapsed >= maxDuration)
                    return FinishCompleted($"达到时长上限（{maxDuration}）。");

                _index = 0;
                _attempts = 0;
            }
        }
    }

    private async Task<NodeRunResult> RunNodeAsync(ActionNode node, CancellationToken stoppingToken)
    {
        switch (node.Kind)
        {
            case ActionKind.Teleport:
                return await RunTeleportAsync((TeleportNode)node, stoppingToken);

            case ActionKind.Delay:
                return await RunDelayAsync((DelayNode)node, stoppingToken);

            case ActionKind.ScriptReplay:
                return await RunScriptReplayAsync((ScriptReplayNode)node, stoppingToken);

            default:
                Emit("execution_faulted", new Dictionary<string, object?>
                {
                    ["node"] = node.Name,
                    ["reason"] = $"未知行动类型：{node.Kind}。",
                });
                return NodeRunResult.Failed;
        }
    }

    private async Task<NodeRunResult> RunTeleportAsync(TeleportNode node, CancellationToken stoppingToken)
    {
        var result = await _teleport.TeleportAsync(node.AnchorName, stoppingToken);
        if (stoppingToken.IsCancellationRequested)
            return NodeRunResult.Canceled;

        if (result.Succeeded)
            return NodeRunResult.Succeeded;

        Emit("stuck_retry", new Dictionary<string, object?>
        {
            ["node"] = node.Name,
            ["reason"] = $"锚点传送失败：{result.Message}",
        });
        return NodeRunResult.Failed;
    }

    private async Task<NodeRunResult> RunDelayAsync(DelayNode node, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(node.Duration, stoppingToken);
            return NodeRunResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return NodeRunResult.Canceled;
        }
    }

    private async Task<NodeRunResult> RunScriptReplayAsync(ScriptReplayNode node, CancellationToken stoppingToken)
    {
        if (_driver is null || _scriptStore is null)
        {
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["reason"] = "脚本回放未装配：缺少输入驱动或脚本存储。",
            });
            return NodeRunResult.Failed;
        }

        RecordedScript? script;
        try
        {
            script = await _scriptStore.LoadAsync(node.ScriptName, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return NodeRunResult.Canceled;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[GraphExecutor] 脚本加载失败「{node.ScriptName}」：{ex.GetBaseException().Message}");
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["reason"] = $"脚本加载失败：{ex.GetBaseException().Message}",
            });
            return NodeRunResult.Failed;
        }

        if (script is null)
        {
            Trace.TraceWarning($"[GraphExecutor] 脚本「{node.ScriptName}」不存在，回退到上游传送。");
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["reason"] = $"脚本「{node.ScriptName}」不存在——请先在「录制」页录制并保存。",
            });
            return NodeRunResult.Failed;
        }

        Trace.TraceInformation($"[GraphExecutor] 脚本回放开始「{node.ScriptName}」（{script.Strokes.Count} 个 stroke）");
        try
        {
            await _replayer.ReplayAsync(_driver, script, stoppingToken);
            Trace.TraceInformation($"[GraphExecutor] 脚本回放完成「{node.ScriptName}」");
            return NodeRunResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning($"[GraphExecutor] 脚本回放被取消「{node.ScriptName}」");
            return NodeRunResult.Canceled;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[GraphExecutor] 脚本回放失败「{node.ScriptName}」：{ex.GetBaseException().Message}");
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["reason"] = $"脚本回放失败：{ex.GetBaseException().Message}",
            });
            return NodeRunResult.Failed;
        }
    }

    private async Task<bool> SegmentPauseAsync(CancellationToken stoppingToken)
    {
        var minMs = Math.Max(0, (int)_options.SegmentPauseMin.TotalMilliseconds);
        var maxMs = Math.Max((int)_options.SegmentPauseMax.TotalMilliseconds, minMs);
        if (maxMs == 0) return true;

        try
        {
            await Task.Delay(_pauseRandom.Next(minMs, maxMs + 1), stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private GraphExecutionResult FinishCompleted(string message)
    {
        var result = new GraphExecutionResult(GraphCompletionReason.Completed, message, _laps, null);
        Reset();
        return result;
    }

    private GraphExecutionResult FinishWithFault(string message, ActionNode node)
    {
        _runWatch.Stop();
        var result = new GraphExecutionResult(GraphCompletionReason.Faulted, message, _laps, node.Name);
        Reset();
        return result;
    }

    private string? FailNode(ActionNode node)
    {
        _attempts++;
        if (_attempts <= _options.MaxNodeRetries)
        {
            Emit("stuck_retry", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["attempt"] = _attempts,
            });
            return null;
        }

        var fallbackIndex = NearestUpstreamTeleport(_chain, _index);
        if (fallbackIndex < 0)
        {
            var message = $"节点「{node.Name}」重试超限，且无上游传送可回退，执行终止。";
            Trace.TraceWarning($"[GraphExecutor] {message}");
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
            });
            return message;
        }

        var anchor = _chain[fallbackIndex];
        Emit("anchor_fallback", new Dictionary<string, object?>
        {
            ["from"] = node.Name,
            ["anchor"] = anchor.Name,
        });
        _index = fallbackIndex;
        _attempts = 0;
        return null;
    }

    private static int IndexOfNode(IReadOnlyList<ActionNode> chain, Guid nodeId)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            if (chain[i].Id == nodeId) return i;
        }

        return -1;
    }

    private static int NearestUpstreamTeleport(IReadOnlyList<ActionNode> chain, int index) => index - 1;

    private void SegmentDone(ActionNode node)
        => Emit("segment_done", new Dictionary<string, object?>
        {
            ["node"] = node.Name,
        });

    private void Emit(string name, IReadOnlyDictionary<string, object?> data)
        => _emitEvent?.Invoke(new ToolEvent(name, data));
}