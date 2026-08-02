using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Dispatch;

/// <summary>
/// 将 <see cref="SceneDispatcher"/> 包装为 <see cref="IRunningTask"/>，
/// 外壳只看到这一个任务。
/// </summary>
public sealed class SceneDispatcherRunningTask : IRunningTask, IDisposable
{
    private readonly Func<ICaptureSource?> _captureSourceProvider;
    private readonly Func<IInputDriver> _driverFactory;
    private readonly Func<bool> _isGameForeground;
    private readonly Func<IReadOnlyList<ISceneDetector>> _detectorFactory;
    private readonly Func<IReadOnlyDictionary<GameScene, ISceneHandler>> _handlerFactory;
    private readonly int _pollIntervalMs;
    private readonly int _debounceFrames;

    private readonly object _gate = new();
    private readonly TaskCompletionSource _idleWhenStopped = new();

    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _stoppedTcs;
    private TaskState _state = TaskState.Idle;
    private IInputDriver? _driver;
    private SceneDispatcher? _dispatcher;

    public SceneDispatcherRunningTask(
        Func<ICaptureSource?> captureSourceProvider,
        Func<IInputDriver> driverFactory,
        Func<bool> isGameForeground,
        Func<IReadOnlyList<ISceneDetector>> detectorFactory,
        Func<IReadOnlyDictionary<GameScene, ISceneHandler>> handlerFactory,
        int pollIntervalMs = 300,
        int debounceFrames = 3)
    {
        _captureSourceProvider = captureSourceProvider ?? throw new ArgumentNullException(nameof(captureSourceProvider));
        _driverFactory = driverFactory ?? throw new ArgumentNullException(nameof(driverFactory));
        _isGameForeground = isGameForeground ?? throw new ArgumentNullException(nameof(isGameForeground));
        _detectorFactory = detectorFactory ?? throw new ArgumentNullException(nameof(detectorFactory));
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
        _pollIntervalMs = pollIntervalMs;
        _debounceFrames = debounceFrames;
        _idleWhenStopped.SetResult();
    }

    public string ToolId => "dispatcher";

    public TaskState State
    {
        get { lock (_gate) { return _state; } }
    }

    public Task WhenStopped
    {
        get { lock (_gate) { return _stoppedTcs?.Task ?? _idleWhenStopped.Task; } }
    }

    public event EventHandler<TaskState>? StateChanged;

    public event EventHandler<ToolEvent>? EventRaised;

    /// <summary>处理器开关变更后请求重新评估激活态（未运行时无操作）。</summary>
    public void RequestRefreshActivation()
    {
        lock (_gate) { _dispatcher?.RequestRefreshActivation(); }
    }

    public void Start()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_state != TaskState.Idle)
                throw new InvalidOperationException($"调度器当前态 {_state}，仅 Idle 可启动");

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token = _cts.Token;
            _state = TaskState.Arming;
        }

        RaiseStateChanged(TaskState.Arming);
        _ = Task.Run(() => RunWorkerAsync(token));
    }

    public void RequestPause(string source = "manual")
    {
        // 调度器暂停＝取消循环（DeactivateCurrent 在 Run 退出时自动执行）
        lock (_gate)
        {
            if (_state != TaskState.Running) return;
            _state = TaskState.Paused;
        }

        _cts?.Cancel();
        RaiseStateChanged(TaskState.Paused);
    }

    public void RequestResume(string source = "manual")
    {
        lock (_gate)
        {
            if (_state != TaskState.Paused) return;
            _state = TaskState.Idle;
        }

        // 恢复＝重新走 Start 流程（重建 CTS + 重新 Arming）
        RaiseStateChanged(TaskState.Idle);
        Start();
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            if (_state is not (TaskState.Arming or TaskState.Running or TaskState.Paused))
                return;
            _state = TaskState.Stopping;
            _cts?.Cancel();
        }

        RaiseStateChanged(TaskState.Stopping);
    }

    public void Dispose()
    {
        lock (_gate) { _cts?.Cancel(); }

        try { WhenStopped.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }

        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }

        _driver?.Dispose();
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        SceneDispatcher? dispatcher = null;
        var detectors = new List<ISceneDetector>();

        try
        {
            // ── Arming：输入设备 ──
            RaiseEvent(new ToolEvent("arming_step", new Dictionary<string, object?>
            {
                ["step"] = "input",
                ["hint"] = "验证 Interception 驱动可用…",
            }));

            var driver = _driverFactory();
            _driver = driver;
            await Task.Run(() => driver.Arm(TimeSpan.FromSeconds(10)), ct);

            // ── Arming：截图源 ──
            RaiseEvent(new ToolEvent("arming_step", new Dictionary<string, object?>
            {
                ["step"] = "capture",
                ["hint"] = "验证截图源可用…",
            }));

            var source = _captureSourceProvider()
                ?? throw new InvalidOperationException("请先在「启动」页开启截图器");

            // ── Arming：检测器 + 处理器 ──
            detectors.AddRange(_detectorFactory());
            var handlers = _handlerFactory();

            var context = new SceneContext
            {
                InputDriver = driver,
                EmitEvent = RaiseEvent,
                IsGameForeground = _isGameForeground,
                CancellationToken = ct,
            };

            dispatcher = new SceneDispatcher(
                source, detectors, handlers, context,
                _pollIntervalMs, _debounceFrames);
            dispatcher.EventRaised += OnDispatcherEvent;
            lock (_gate) { _dispatcher = dispatcher; }

            // ── 进入 Running ──
            bool entered;
            lock (_gate)
            {
                entered = _state == TaskState.Arming;
                if (entered) _state = TaskState.Running;
            }

            if (!entered) return;
            RaiseStateChanged(TaskState.Running);

            await Task.Run(() => dispatcher.Run(ct), ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SafeRaiseEvent(new ToolEvent("arming_failed", new Dictionary<string, object?>
            {
                ["error"] = ex.GetBaseException().Message,
                ["remedy"] = "检查截图器和 Interception 驱动状态后重试",
            }));
        }
        finally
        {
            if (dispatcher is not null)
                dispatcher.EventRaised -= OnDispatcherEvent;

            lock (_gate) { _dispatcher = null; }

            foreach (var d in detectors)
                (d as IDisposable)?.Dispose();

            lock (_gate) { _state = TaskState.Idle; }
            RaiseStateChanged(TaskState.Idle);
            _stoppedTcs?.TrySetResult();
        }
    }

    private void OnDispatcherEvent(object? sender, ToolEvent e) => RaiseEvent(e);

    private void RaiseStateChanged(TaskState state) => StateChanged?.Invoke(this, state);

    private void RaiseEvent(ToolEvent toolEvent) => EventRaised?.Invoke(this, toolEvent);

    private void SafeRaiseEvent(ToolEvent toolEvent)
    {
        try { RaiseEvent(toolEvent); }
        catch { }
    }
}
