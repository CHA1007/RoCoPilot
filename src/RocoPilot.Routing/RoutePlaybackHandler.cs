using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Settings;
using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Routing;

public sealed class RoutePlaybackHandler : ISceneHandler, IDisposable
{
    private const string TeleportTemplatePath = "assets/templates/map/teleport.png";
    private const string FastTravelToolId = "fast-travel";

    private readonly RouteStore _store;
    private readonly ISettingsStore _settings;
    private readonly Func<ICaptureSource?> _captureSourceProvider;
    private readonly Func<GameScene> _currentScene;

    private readonly object _gate = new();
    private SceneContext? _context;
    private IInputDriver? _builtDriver;
    private GraphExecutor? _executor;
    private RouteGraph? _graph;
    private TeleportSensor? _sensor;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public RoutePlaybackHandler(
        RouteStore store,
        ISettingsStore settings,
        Func<ICaptureSource?> captureSourceProvider,
        Func<GameScene> currentScene)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _captureSourceProvider = captureSourceProvider ?? throw new ArgumentNullException(nameof(captureSourceProvider));
        _currentScene = currentScene ?? throw new ArgumentNullException(nameof(currentScene));
    }

    public GameScene Scene => GameScene.OpenWorld;

    public bool IsEnabled { get; set; }

    public void Activate(SceneContext context)
    {
        lock (_gate)
        {
            if (_runTask is not null) return;

            RebuildPipelineIfNeeded(context);
            if (_executor is null)
            {
                context.EmitEvent(new ToolEvent("route_playback_fault", new Dictionary<string, object?>
                {
                    ["error"] = "截图源不可用，路线回放无法启动。",
                }));
                return;
            }

            _context = context;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var token = _cts.Token;
            _runTask = Task.Run(() => RunAsync(token));
        }
    }

    public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
        => _runTask is not null && !_runTask.IsCompleted;

    public void Deactivate()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_gate)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
            _context = null;
        }

        cts?.Cancel();

        try { runTask?.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }

        cts?.Dispose();
    }

    public void Dispose()
    {
        Deactivate();
        _sensor?.Dispose();
        _sensor = null;
    }

    private void RebuildPipelineIfNeeded(SceneContext context)
    {
        if (_executor is not null && ReferenceEquals(_builtDriver, context.InputDriver))
            return;

        _sensor?.Dispose();
        _sensor = null;
        _executor = null;
        _graph = null;
        _builtDriver = null;

        var source = _captureSourceProvider();
        if (source is null) return;

        var teleportSettings = _settings.GetToolSettings(
            FastTravelToolId, typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
                               ?? new FastTravelSettings();

        var playbackSettings = _settings.GetToolSettings(
            RoutePlaybackSettings.ToolId, typeof(RoutePlaybackSettings), () => new RoutePlaybackSettings()) as RoutePlaybackSettings
                               ?? new RoutePlaybackSettings();
        playbackSettings.SanitizeInPlace();
        _settings.SetToolSettings(RoutePlaybackSettings.ToolId, playbackSettings);

        _sensor = TeleportSensor.TryCreate(TeleportTemplatePath);
        var player = new RoutePlayer(context.InputDriver, source, playbackSettings.ToPlayerOptions());
        var guide = new PoiTeleportGuide(
            grabFrame: () => source.TryGrabLatest(out var frame) ? frame : null,
            inputDriver: context.InputDriver,
            teleportSensor: _sensor,
            teleportSettings: teleportSettings,
            frameToScreen: GameFrameMapper.Create(source),
            isGameForeground: context.IsGameForeground,
            emitEvent: context.EmitEvent,
            anchorListProvider: ct => _store.LoadAnchorsAsync(ct));

        _executor = new GraphExecutor(player, guide, _store.LoadAsync, _currentScene, context.EmitEvent, playbackSettings.ToExecutorOptions());
        _builtDriver = context.InputDriver;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var emit = _context?.EmitEvent;
        try
        {
            var executor = _executor;
            if (executor is null || emit is null) return;

            if (_graph is null)
            {
                RouteGraph graph;
                try
                {
                    graph = await _store.LoadGraphAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (FileNotFoundException)
                {
                    emit(new ToolEvent("route_playback_fault", new Dictionary<string, object?>
                    {
                        ["error"] = "尚未配置执行图——到「路线」页添加节点并连线后再运行。",
                    }));
                    return;
                }
                catch (Exception ex)
                {
                    emit(new ToolEvent("route_playback_fault", new Dictionary<string, object?>
                    {
                        ["error"] = $"执行图加载失败：{ex.Message}",
                    }));
                    return;
                }

                _graph = graph;
            }

            var result = await executor.RunAsync(_graph, ct);

            if (result.Reason == GraphCompletionReason.Stopped)
            {
                emit(new ToolEvent("route_suspended", new Dictionary<string, object?>
                {
                    ["laps"] = result.LapsCompleted,
                }));
                return;
            }

            emit(new ToolEvent("graph_finished", new Dictionary<string, object?>
            {
                ["reason"] = result.Reason.ToString(),
                ["message"] = result.Message,
                ["laps"] = result.LapsCompleted,
                ["failed_node"] = result.FailedNode,
            }));
            executor.Reset();
            _graph = null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            emit?.Invoke(new ToolEvent("fault", new Dictionary<string, object?>
            {
                ["error"] = ex.GetBaseException().Message,
                ["source"] = "route_playback",
            }));
            _executor?.Reset();
            _graph = null;
        }
    }
}
