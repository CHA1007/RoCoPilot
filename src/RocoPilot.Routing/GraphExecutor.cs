using System.Diagnostics;
using RocoPilot.Core;
using RocoPilot.Dispatch;

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

    public TimeSpan ScenePollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan SegmentPauseMin { get; init; } = TimeSpan.FromMilliseconds(1500);

    public TimeSpan SegmentPauseMax { get; init; } = TimeSpan.FromMilliseconds(4000);
}

public sealed class GraphExecutor
{
    private readonly RoutePlayer _player;
    private readonly PoiTeleportGuide _teleport;
    private readonly Func<string, CancellationToken, Task<Route>> _loadRoute;
    private readonly Func<GameScene> _currentScene;
    private readonly Action<ToolEvent>? _emitEvent;
    private readonly GraphExecutorOptions _options;
    private readonly Random _pauseRandom = new();
    private readonly Stopwatch _runWatch = new();

    private RouteGraph? _graph;
    private IReadOnlyList<RouteNode> _chain = [];
    private int _index;
    private int _laps;
    private int _attempts;

    public GraphExecutor(
        RoutePlayer player,
        PoiTeleportGuide teleport,
        Func<string, CancellationToken, Task<Route>> loadRoute,
        Func<GameScene> currentScene,
        Action<ToolEvent>? emitEvent = null,
        GraphExecutorOptions? options = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _teleport = teleport ?? throw new ArgumentNullException(nameof(teleport));
        _loadRoute = loadRoute ?? throw new ArgumentNullException(nameof(loadRoute));
        _currentScene = currentScene ?? throw new ArgumentNullException(nameof(currentScene));
        _emitEvent = emitEvent;
        _options = options ?? new GraphExecutorOptions();
    }

    public RouteGraph? CurrentGraph => _graph;

    public void Reset()
    {
        _graph = null;
        _chain = [];
        _index = 0;
        _laps = 0;
        _attempts = 0;
        _runWatch.Reset();
    }

    public async Task<GraphExecutionResult> RunAsync(RouteGraph graph, CancellationToken stoppingToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (!ReferenceEquals(graph, _graph))
        {
            IReadOnlyList<RouteNode> chain;
            try
            {
                chain = graph.OrderedChain();
            }
            catch (InvalidOperationException ex)
            {
                return new GraphExecutionResult(GraphCompletionReason.Faulted, ex.Message, 0, null);
            }

            _graph = graph;
            _chain = chain;
            _index = 0;
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
                ["kind"] = node.Kind.ToString(),
            });

            switch (node.Kind)
            {
                case RouteNodeKind.Anchor:
                {
                    var teleportResult = await _teleport.TeleportAsync(node.AnchorName!, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        _runWatch.Stop();
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                    }

                    if (teleportResult.Succeeded)
                    {
                        SegmentDone(node);
                        if (!await SegmentPauseAsync(stoppingToken))
                        {
                            _runWatch.Stop();
                            return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                        }
                        _index++;
                        _attempts = 0;
                        break;
                    }

                    if (FailNode(node, $"锚点传送失败：{teleportResult.Message}") is { } fault)
                        return FinishWithFault(fault, node);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        _runWatch.Stop();
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                    }
                    break;
                }

                case RouteNodeKind.Playback:
                {
                    Route route;
                    try
                    {
                        route = await _loadRoute(node.RouteName!, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _runWatch.Stop();
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                    }
                    catch (Exception ex)
                    {
                        if (FailNode(node, $"路线加载失败：{ex.Message}") is { } fault)
                            return FinishWithFault(fault, node);
                        if (stoppingToken.IsCancellationRequested)
                        {
                            _runWatch.Stop();
                            return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                        }
                        break;
                    }

                    var playback = await PlayWithBattleSuspendAsync(route, node, stoppingToken);
                    switch (playback.Outcome)
                    {
                        case PlaybackOutcome.Completed:
                            SegmentDone(node);
                            if (!await SegmentPauseAsync(stoppingToken))
                            {
                                _runWatch.Stop();
                                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                            }
                            _index++;
                            _attempts = 0;
                            break;

                        case PlaybackOutcome.Stopped:
                            _runWatch.Stop();
                            return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);

                        case PlaybackOutcome.Stuck:
                            if (FailNode(node, "回放卡死（画面长时间无变化）。") is { } fault)
                                return FinishWithFault(fault, node);
                            if (stoppingToken.IsCancellationRequested)
                            {
                                _runWatch.Stop();
                                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", _laps, null);
                            }
                            break;
                    }

                    break;
                }

                case RouteNodeKind.Loop:
                {
                    _laps++;
                    Emit("loop_lap", new Dictionary<string, object?>
                    {
                        ["lap"] = _laps,
                        ["elapsed_ms"] = _runWatch.Elapsed.TotalMilliseconds,
                    });

                    if (node.MaxLaps is { } maxLaps && _laps >= maxLaps)
                        return FinishCompleted($"达到圈数上限（{maxLaps} 圈）。");
                    if (node.MaxDuration is { } maxDuration && _runWatch.Elapsed >= maxDuration)
                        return FinishCompleted($"达到时长上限（{maxDuration}）。");

                    _index = 0;
                    _attempts = 0;
                    break;
                }
            }

            if (_index >= _chain.Count)
                return FinishCompleted("执行图运行完成。");
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

    private GraphExecutionResult FinishWithFault(string message, RouteNode node)
    {
        _runWatch.Stop();
        var result = new GraphExecutionResult(GraphCompletionReason.Faulted, message, _laps, node.Name);
        Reset();
        return result;
    }

    private async Task<PlaybackResult> PlayWithBattleSuspendAsync(Route route, RouteNode node, CancellationToken stoppingToken)
    {
        var playTask = _player.PlayAsync(route, 0, stoppingToken);
        var suspended = false;
        var pollMs = (int)Math.Max(50, _options.ScenePollInterval.TotalMilliseconds);

        while (!playTask.IsCompleted)
        {
            try
            {
                await Task.Delay(pollMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            switch (_currentScene())
            {
                case GameScene.Battle when !suspended:
                    suspended = true;
                    _player.Pause();
                    Emit("battle_suspend", new Dictionary<string, object?>
                    {
                        ["node"] = node.Name,
                        ["offset_ms"] = _player.Progress.OffsetMs,
                    });
                    break;

                case GameScene.OpenWorld when suspended:
                    suspended = false;
                    Emit("battle_resume", new Dictionary<string, object?>
                    {
                        ["node"] = node.Name,
                        ["offset_ms"] = _player.Progress.OffsetMs,
                    });
                    _player.Resume();
                    break;
            }
        }

        return await playTask;
    }

    private string? FailNode(RouteNode node, string reason)
    {
        _attempts++;
        if (_attempts <= _options.MaxNodeRetries)
        {
            Emit("stuck_retry", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["attempt"] = _attempts,
                ["reason"] = reason,
            });
            return null;
        }

        var fallbackIndex = NearestUpstreamAnchor(_chain, _index);
        if (fallbackIndex < 0)
        {
            var message = $"节点「{node.Name}」重试超限（{reason}），且无上游锚点可回退，执行终止。";
            Trace.TraceWarning($"[GraphExecutor] {message}");
            Emit("execution_faulted", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["reason"] = reason,
            });
            return message;
        }

        var anchor = _chain[fallbackIndex];
        Emit("anchor_fallback", new Dictionary<string, object?>
        {
            ["from"] = node.Name,
            ["anchor"] = anchor.Name,
            ["reason"] = reason,
        });
        _index = fallbackIndex;
        _attempts = 0;
        return null;
    }

    private static int NearestUpstreamAnchor(IReadOnlyList<RouteNode> chain, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (chain[i].Kind == RouteNodeKind.Anchor)
                return i;
        }

        return -1;
    }

    private void SegmentDone(RouteNode node)
        => Emit("segment_done", new Dictionary<string, object?>
        {
            ["node"] = node.Name,
            ["kind"] = node.Kind.ToString(),
        });

    private void Emit(string name, IReadOnlyDictionary<string, object?> data)
        => _emitEvent?.Invoke(new ToolEvent(name, data));
}
