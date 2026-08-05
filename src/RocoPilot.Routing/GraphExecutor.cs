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
}

public sealed class GraphExecutor
{
    private readonly RoutePlayer _player;
    private readonly PoiTeleportGuide _teleport;
    private readonly Func<string, CancellationToken, Task<Route>> _loadRoute;
    private readonly Func<GameScene> _currentScene;
    private readonly Action<ToolEvent>? _emitEvent;
    private readonly GraphExecutorOptions _options;

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

    public async Task<GraphExecutionResult> RunAsync(RouteGraph graph, CancellationToken stoppingToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        IReadOnlyList<RouteNode> chain;
        try
        {
            chain = graph.OrderedChain();
        }
        catch (InvalidOperationException ex)
        {
            return new GraphExecutionResult(GraphCompletionReason.Faulted, ex.Message, 0, null);
        }

        if (chain.Count == 0)
            return new GraphExecutionResult(GraphCompletionReason.Faulted, "执行图为空。", 0, null);

        var runWatch = Stopwatch.StartNew();
        var laps = 0;
        var index = 0;
        var attempts = 0;

        Emit("route_started", new Dictionary<string, object?>
        {
            ["graph"] = graph.Name,
            ["nodes"] = chain.Count,
        });

        while (true)
        {
            if (stoppingToken.IsCancellationRequested)
                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);

            var node = chain[index];

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
                    var teleportResult = await Task.Run(() => _teleport.Teleport(node.PoiName!, stoppingToken), CancellationToken.None);
                    if (stoppingToken.IsCancellationRequested)
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);

                    if (teleportResult.Succeeded)
                    {
                        SegmentDone(node);
                        index++;
                        attempts = 0;
                        break;
                    }

                    if (FailNode(node, $"锚点传送失败：{teleportResult.Message}", chain, ref index, ref attempts) is { } fault)
                        return new GraphExecutionResult(GraphCompletionReason.Faulted, fault, laps, node.Name);
                    if (stoppingToken.IsCancellationRequested)
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);
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
                        return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);
                    }
                    catch (Exception ex)
                    {
                        if (FailNode(node, $"路线加载失败：{ex.Message}", chain, ref index, ref attempts) is { } fault)
                            return new GraphExecutionResult(GraphCompletionReason.Faulted, fault, laps, node.Name);
                        if (stoppingToken.IsCancellationRequested)
                            return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);
                        break;
                    }

                    var playback = await PlayWithBattleSuspendAsync(route, node, stoppingToken);
                    switch (playback.Outcome)
                    {
                        case PlaybackOutcome.Completed:
                            SegmentDone(node);
                            index++;
                            attempts = 0;
                            break;

                        case PlaybackOutcome.Stopped:
                            return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);

                        case PlaybackOutcome.Stuck:
                            if (FailNode(node, "回放卡死（画面长时间无变化）。", chain, ref index, ref attempts) is { } fault)
                                return new GraphExecutionResult(GraphCompletionReason.Faulted, fault, laps, node.Name);
                            if (stoppingToken.IsCancellationRequested)
                                return new GraphExecutionResult(GraphCompletionReason.Stopped, "执行已停止。", laps, null);
                            break;
                    }

                    break;
                }

                case RouteNodeKind.Loop:
                {
                    laps++;
                    Emit("loop_lap", new Dictionary<string, object?>
                    {
                        ["lap"] = laps,
                        ["elapsed_ms"] = runWatch.Elapsed.TotalMilliseconds,
                    });

                    if (node.MaxLaps is { } maxLaps && laps >= maxLaps)
                        return new GraphExecutionResult(GraphCompletionReason.Completed, $"达到圈数上限（{maxLaps} 圈）。", laps, null);
                    if (node.MaxDuration is { } maxDuration && runWatch.Elapsed >= maxDuration)
                        return new GraphExecutionResult(GraphCompletionReason.Completed, $"达到时长上限（{maxDuration}）。", laps, null);

                    index = 0;
                    attempts = 0;
                    break;
                }
            }

            if (index >= chain.Count)
                return new GraphExecutionResult(GraphCompletionReason.Completed, "执行图运行完成。", laps, null);
        }
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

    private string? FailNode(
        RouteNode node,
        string reason,
        IReadOnlyList<RouteNode> chain,
        ref int index,
        ref int attempts)
    {
        attempts++;
        if (attempts <= _options.MaxNodeRetries)
        {
            Emit("stuck_retry", new Dictionary<string, object?>
            {
                ["node"] = node.Name,
                ["attempt"] = attempts,
                ["reason"] = reason,
            });
            return null;
        }

        var fallbackIndex = NearestUpstreamAnchor(chain, index);
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

        var anchor = chain[fallbackIndex];
        Emit("anchor_fallback", new Dictionary<string, object?>
        {
            ["from"] = node.Name,
            ["anchor"] = anchor.Name,
            ["reason"] = reason,
        });
        index = fallbackIndex;
        attempts = 0;
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
