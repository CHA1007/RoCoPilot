using RocoPilot.Capture;
using RocoPilot.Core;

namespace RocoPilot.Dispatch;

/// <summary>
/// 中央调度器：持续抓帧 → 场景检测 → 自动切换已启用的场景处理器。
/// 同一时刻最多一个 handler 活跃（单输入源）。
/// </summary>
public sealed class SceneDispatcher
{
    private const int SleepChunkMs = 100;

    private readonly ICaptureSource _captureSource;
    private readonly IReadOnlyList<ISceneDetector> _detectors;
    private readonly IReadOnlyDictionary<GameScene, ISceneHandler> _handlers;
    private readonly SceneContext _context;
    private readonly int _pollIntervalMs;
    private readonly int _debounceFrames;

    private GameScene _currentScene = GameScene.Unknown;
    private ISceneHandler? _activeHandler;
    private GameScene _pendingScene = GameScene.Unknown;
    private int _pendingCount;

    public SceneDispatcher(
        ICaptureSource captureSource,
        IReadOnlyList<ISceneDetector> detectors,
        IReadOnlyDictionary<GameScene, ISceneHandler> handlers,
        SceneContext context,
        int pollIntervalMs = 300,
        int debounceFrames = 3)
    {
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _detectors = detectors ?? throw new ArgumentNullException(nameof(detectors));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pollIntervalMs = Math.Max(50, pollIntervalMs);
        _debounceFrames = Math.Max(1, debounceFrames);
    }

    public GameScene CurrentScene => _currentScene;

    public event EventHandler<ToolEvent>? EventRaised;

    /// <summary>调度主循环，阻塞直到取消。</summary>
    public void Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // 失焦门控：游戏窗口不在前台时挂起，不发任何输入
            if (!_context.IsGameForeground())
            {
                DeactivateCurrent();
                SleepInterruptible(_pollIntervalMs, cancellationToken);
                continue;
            }

            if (!_captureSource.TryGrabLatest(out var frame) || frame is null)
            {
                SleepInterruptible(_pollIntervalMs, cancellationToken);
                continue;
            }

            try
            {
                var pixels = frame.Pixels;
                if (pixels.IsEmpty)
                {
                    SleepInterruptible(_pollIntervalMs, cancellationToken);
                    continue;
                }

                var scene = DetectScene(pixels, frame.Width, frame.Height);
                UpdateScene(scene, pixels, frame.Width, frame.Height);
            }
            finally
            {
                frame.Dispose();
            }

            SleepInterruptible(_pollIntervalMs, cancellationToken);
        }

        DeactivateCurrent();
    }

    /// <summary>对所有检测器投票，取置信度最高者。</summary>
    private GameScene DetectScene(ReadOnlySpan<byte> pixels, int width, int height)
    {
        var bestScene = GameScene.Unknown;
        var bestScore = 0f;

        foreach (var detector in _detectors)
        {
            var score = detector.Detect(pixels, width, height);
            if (score > bestScore)
            {
                bestScore = score;
                bestScene = detector.Scene;
            }
        }

        return bestScene;
    }

    /// <summary>去抖 + 场景切换。</summary>
    private void UpdateScene(GameScene detected, ReadOnlySpan<byte> pixels, int width, int height)
    {
        // 与当前场景一致，重置去抖计数
        if (detected == _currentScene)
        {
            _pendingScene = GameScene.Unknown;
            _pendingCount = 0;

            // 持续喂帧给活跃 handler
            _activeHandler?.Handle(pixels, width, height);
            return;
        }

        // 去抖：连续 N 帧确认才切换
        if (detected == _pendingScene)
        {
            _pendingCount++;
        }
        else
        {
            _pendingScene = detected;
            _pendingCount = 1;
        }

        if (_pendingCount < _debounceFrames)
            return;

        // 确认切换
        var previous = _currentScene;
        DeactivateCurrent();
        _currentScene = detected;
        _pendingScene = GameScene.Unknown;
        _pendingCount = 0;

        Emit("scene_changed", new Dictionary<string, object?>
        {
            ["from"] = previous.ToString(),
            ["to"] = detected.ToString(),
        });

        ActivateHandler(detected);

        // 切换后立即喂一帧
        _activeHandler?.Handle(pixels, width, height);
    }

    private void ActivateHandler(GameScene scene)
    {
        if (!_handlers.TryGetValue(scene, out var handler))
            return;

        if (!handler.IsEnabled)
        {
            Emit("handler_disabled", new Dictionary<string, object?>
            {
                ["scene"] = scene.ToString(),
            });
            return;
        }

        handler.Activate(_context);
        _activeHandler = handler;

        Emit("handler_activated", new Dictionary<string, object?>
        {
            ["scene"] = scene.ToString(),
        });
    }

    private void DeactivateCurrent()
    {
        if (_activeHandler is null)
            return;

        var scene = _activeHandler.Scene;
        _activeHandler.Deactivate();
        _activeHandler = null;

        Emit("handler_deactivated", new Dictionary<string, object?>
        {
            ["scene"] = scene.ToString(),
        });
    }

    private void Emit(string name, IReadOnlyDictionary<string, object?>? data = null)
    {
        var handlers = EventRaised;
        if (handlers is null) return;

        var toolEvent = new ToolEvent(name, data);
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<ToolEvent>)handler)(this, toolEvent);
            }
            catch
            {
                // 事件订阅者异常不影响调度循环
            }
        }
    }

    private static void SleepInterruptible(int milliseconds, CancellationToken cancellationToken)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Math.Min(SleepChunkMs, remaining);
            Thread.Sleep(chunk);
            remaining -= chunk;
        }
    }
}
