using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Dispatch;

public sealed class SceneDispatcherRunningTask : RunningTaskBase
{
    private readonly Func<ICaptureSource?> _captureSourceProvider;
    private readonly Func<IInputDriver> _driverFactory;
    private readonly Func<bool> _isGameForeground;
    private readonly Func<IReadOnlyList<ISceneDetector>> _detectorFactory;
    private readonly Func<IReadOnlyDictionary<GameScene, ISceneHandler>> _handlerFactory;
    private readonly int _pollIntervalMs;
    private readonly int _debounceFrames;

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
    }

    public override string ToolId => "dispatcher";

    public void RequestRefreshActivation()
    {
        lock (Gate) { _dispatcher?.RequestRefreshActivation(); }
    }

    public override void RequestPause(string source = "manual")
    {
        lock (Gate)
        {
            if (CurrentState != TaskState.Running) return;
            CurrentState = TaskState.Paused;
        }

        CancelWorker();
        RaiseStateChanged(TaskState.Paused);
    }

    public override void RequestResume(string source = "manual")
    {
        lock (Gate)
        {
            if (CurrentState != TaskState.Paused) return;
            CurrentState = TaskState.Idle;
        }

        RaiseStateChanged(TaskState.Idle);
        Start();
    }

    protected override void DisposeCore() => _driver?.Dispose();

    protected override async Task RunWorkerAsync(CancellationToken ct)
    {
        SceneDispatcher? dispatcher = null;
        var detectors = new List<ISceneDetector>();
        ICaptureSource? source = null;

        try
        {
            string ArmingRemedy(Exception _) => "检查截图器和 Interception 驱动状态后重试";

            var steps = new[]
            {
                new ArmingStep("input", "验证 Interception 驱动可用…", async token =>
                {
                    var driver = _driverFactory();
                    _driver = driver;
                    await Task.Run(() => driver.Arm(), token);
                }) { Remedy = ArmingRemedy },
                new ArmingStep("capture", "验证截图源可用…", _ =>
                {
                    source = _captureSourceProvider()
                        ?? throw new InvalidOperationException("请先在「启动」页开启截图器");
                    return Task.CompletedTask;
                }) { Remedy = ArmingRemedy },
            };

            if (!await Arming.ExecuteAsync(steps, SafeRaiseEvent, ct)) return;

            detectors.AddRange(_detectorFactory());
            var handlers = _handlerFactory();

            var context = new SceneContext
            {
                InputDriver = _driver!,
                EmitEvent = RaiseEvent,
                IsGameForeground = _isGameForeground,
                CancellationToken = ct,
            };

            dispatcher = new SceneDispatcher(
                source!, detectors, handlers, context,
                _pollIntervalMs, _debounceFrames);
            dispatcher.EventRaised += OnDispatcherEvent;
            lock (Gate) { _dispatcher = dispatcher; }

            if (!TryEnterRunning()) return;
            RaiseStateChanged(TaskState.Running);

            await Task.Run(() => dispatcher.Run(ct), ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SafeRaiseEvent(new ToolEvent("fault", new Dictionary<string, object?>
            {
                ["error"] = ex.GetBaseException().Message,
                ["source"] = "dispatcher_run",
            }));
        }
        finally
        {
            if (dispatcher is not null)
                dispatcher.EventRaised -= OnDispatcherEvent;

            lock (Gate) { _dispatcher = null; }

            foreach (var d in detectors)
                (d as IDisposable)?.Dispose();

            FinishStopped();
        }
    }

    private void OnDispatcherEvent(object? sender, ToolEvent e) => RaiseEvent(e);
}
