using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed class CatchPipeline : ICatchPipeline
{
    private const int PollChunkMs = 100;

    private readonly CatchPipelineSpec _spec;
    private readonly CatchPipelineFactories _factories;
    private readonly CalibrationCache _cache = new();

    private IDetector? _detector;
    private IInputDriver? _driver;
    private ICaptureSource? _source;
    private StreamingTargetSensor? _sensor;
    private CenteringController? _controller;
    private CatchLoopEngine? _engine;
    private CatchEventBus? _bus;
    private JsonlEventSink? _sink;
    private FailureSceneRecorder? _recorder;
    private IntPtr _gameWindow;
    private Task? _armTask;

    public CatchPipeline(CatchPipelineSpec? spec = null, CatchPipelineFactories? factories = null)
    {
        _spec = spec ?? new CatchPipelineSpec();
        _factories = factories ?? new CatchPipelineFactories();
        if (_spec.Centering.SensitivityPpc > 0)
        {
            _cache.Store(1, _spec.Centering.SensitivityPpc);
        }

        ArmingSteps = [DetectorStep(), InputStep(), CaptureStep()];
    }

    public IReadOnlyList<ArmingStep> ArmingSteps { get; }

    public Func<bool> InputGate => () => _gameWindow != IntPtr.Zero && _factories.ForegroundWindow() == _gameWindow;

    public IntPtr GameWindow => _gameWindow;

    public CatchEventBus Bus => _bus ?? throw new InvalidOperationException("事件总线在 Arming 成功前不存在");

    public void Run(CancellationToken cancellationToken) =>
        (_engine ?? throw new InvalidOperationException("Run 须先完成 Arming")).Run(cancellationToken);

    public bool Pause(string source = "manual") => _engine is not null && _engine.Pause(source);

    public bool Resume(string source = "manual") => _engine is not null && _engine.Resume(source);

    public void SetSensing(bool enabled)
    {
        if (enabled)
        {
            _sensor?.Resume();
        }
        else
        {
            _sensor?.Suspend();
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        if (_sensor is not null)
        {
            _sensor.RecognitionFlipped -= OnRecognitionFlipped;
        }

        _recorder?.Dispose();
        _sensor?.Dispose();
        _source?.Stop();
        _source?.Dispose();
        (_detector as IDisposable)?.Dispose();
        if (_armTask is not null)
        {
            try
            {
                _armTask.Wait(_spec.DeviceDiscoveryTimeout + TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        _driver?.Dispose();
        _sink?.Dispose();
    }

    private ArmingStep DetectorStep() => new(
        "detector",
        "检测器加载中（ONNX 会话，首次约数百毫秒）…",
        async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _detector = await Task.Run(() => _factories.Detector(_spec.Detection, _spec.UseGpu));
            cancellationToken.ThrowIfCancellationRequested();
        })
    {
        Remedy = _ => "确认模型文件 assets/models/yolo11n-roco.onnx 在输出目录在位（构建期资产，重装即可）",
    };

    private ArmingStep InputStep() => new(
        "input",
        "设备发现：10 秒内动一下鼠标（收得到事件＝驱动真在设备栈）…",
        async cancellationToken =>
        {
            var driver = _factories.Driver(_spec.InputBackend);
            _driver = driver;
            _armTask = Task.Run(() => driver.Arm(_spec.DeviceDiscoveryTimeout));
            try
            {
                await _armTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await _armTask.ContinueWith(static _ => { }).WaitAsync(
                    _spec.DeviceDiscoveryTimeout + TimeSpan.FromSeconds(2));
                throw;
            }
        })
    {
        Remedy = _ => "装好并跑起 Interception 驱动（管理员 sc query interception 应 RUNNING），再于 10 秒内动一下鼠标重试",
    };

    private ArmingStep CaptureStep() => new(
        "capture",
        "正在启动截图与识别，等待画面中出现稳定精灵…",
        async cancellationToken =>
        {
            _gameWindow = _factories.WindowFinder(_spec.WindowTitleSubstring);
            if (!string.IsNullOrWhiteSpace(_spec.WindowTitleSubstring) && _gameWindow == IntPtr.Zero)
            {
                throw new CaptureException($"没有标题含「{_spec.WindowTitleSubstring}」的可见窗口");
            }

            _source = await _factories.Capture(new CaptureOptions
            {
                WindowTitleSubstring = _spec.WindowTitleSubstring,
                Backend = string.IsNullOrWhiteSpace(_spec.WindowTitleSubstring)
                    ? CaptureBackendMode.Auto
                    : CaptureBackendMode.ForceWgcWindow,
            }, cancellationToken);

            var retainFrames = _spec.SessionLogDirectory is not null;
            _sensor = new StreamingTargetSensor(_source, _detector!, new StabilityGate(
                _spec.Detection.StableFrames, _spec.Detection.StabilitySpreadPx, _spec.Detection.AssociationRadiusPx),
                retainFrames, _spec.DetectionIntervalMs);
            _sensor.Start();
            if (_spec.SessionLogDirectory is { } sessionDir)
            {
                var store = new SceneStore(Path.Combine(sessionDir, "scenes"), _factories.SceneImageEncoder());
                _recorder = new FailureSceneRecorder(store, () => _sensor.TrySnapshot(out var snap) ? snap : null);
                _sensor.RecognitionFlipped += OnRecognitionFlipped;
            }

            try
            {
                await WaitFirstStableTarget(cancellationToken);
            }
            catch (LoopException ex)
            {
                CaptureCalibrationScene(ex);
                throw;
            }

            _sink = _spec.SessionLogDirectory is { } dir
                ? new JsonlEventSink(Path.Combine(dir, "events.jsonl"))
                : null;
            _controller = new CenteringController(
                _spec.Centering, _sensor, _driver!, _cache, inputGate: InputGate);
            _bus = new CatchEventBus(new CatchCounters(), _sink);
            _recorder?.AttachBus(_bus);
            _engine = new CatchLoopEngine(
                _spec.Loop, _spec.Mode, _sensor, _driver!, _controller, _bus, inputGate: InputGate);
        })
    {
        Remedy = ex => ex is CaptureException
            ? "把《洛克王国：世界》客户端开起来（窗口标题含「洛克王国」）再重试"
            : "把镜头转向一只野外精灵再重试",
    };

    private void CaptureCalibrationScene(Exception cause)
    {
        try
        {
            _recorder?.Capture("calibration", new ToolEvent("arming_failed", new Dictionary<string, object?>
            {
                ["step"] = "calibration",
                ["error"] = cause.GetBaseException().Message,
            }));
        }
        catch
        {
        }
    }

    private void OnRecognitionFlipped(object? sender, RecognitionFlip flip)
    {
        try
        {
            _recorder?.Capture("jump", new ToolEvent("recognition_flipped", new Dictionary<string, object?>
            {
                ["track_id"] = flip.TrackId,
                ["from"] = flip.PreviousClass,
                ["to"] = flip.Current.Latest.ClassName,
                ["conf"] = Math.Round(flip.Current.Latest.Confidence, 3),
            }));
        }
        catch
        {
        }
    }

    private async Task WaitFirstStableTarget(CancellationToken cancellationToken)
    {
        var waited = TimeSpan.Zero;
        var chunk = TimeSpan.FromMilliseconds(PollChunkMs);
        while (_sensor!.ObserveStable().Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (waited >= _spec.FirstStableTargetTimeout)
            {
                throw new LoopException(
                    $"超时 {_spec.FirstStableTargetTimeout.TotalSeconds:0}s 未见稳定精灵（检测 / 捕获已就绪）");
            }

            await Task.Delay(chunk, cancellationToken);
            waited += chunk;
        }
    }
}
