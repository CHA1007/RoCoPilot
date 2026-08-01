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
    private bool _ownsSource;
    private StreamingTargetSensor? _sensor;
    private CenteringController? _controller;
    private CatchLoopEngine? _engine;
    private CatchEventBus? _bus;
    private JsonlEventSink? _sink;
    private FailureSceneRecorder? _recorder;
    private IntPtr _gameWindow;
    private Task? _armTask;
    private AutoCalibrator.CalibrationResult? _calibratedPpc;

    public CatchPipeline(CatchPipelineSpec? spec = null, CatchPipelineFactories? factories = null)
    {
        _spec = spec ?? new CatchPipelineSpec();
        _factories = factories ?? new CatchPipelineFactories();
        if (_spec.Centering.SensitivityPpc > 0)
        {
            _cache.Store(1, _spec.Centering.SensitivityPpc);
        }

        var steps = new List<ArmingStep> { DetectorStep(), InputStep(), CaptureStep() };
        if (_spec.CalibrateBeforeThrow) steps.Add(CalibrationStep());
        steps.Add(EngineStep());
        ArmingSteps = steps;
    }

    public IReadOnlyList<ArmingStep> ArmingSteps { get; }

    public Func<bool> InputGate => () => _gameWindow != IntPtr.Zero && _factories.IsGameForeground();

    public IntPtr GameWindow => _gameWindow;

    public CatchEventBus Bus => _bus ?? throw new InvalidOperationException("事件总线在 Arming 成功前不存在");

    public IReadOnlyList<StableTarget> ObserveDetections() =>
        _sensor?.ObserveStable() ?? [];

    public (int Width, int Height) SensorFrameSize =>
        _sensor?.LatestFrameSize ?? (0, 0);

    public int ActiveTrackId => _engine?.ActiveTrackId ?? -1;

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
        if (_ownsSource)
        {
            _source?.Stop();
            _source?.Dispose();
        }
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
        "设备初始化：验证 Interception 驱动可用…",
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
        "正在启动截图与识别…",
        async cancellationToken =>
        {
            _gameWindow = WindowFinder.FindByProcessName(WindowFinder.GameProcessName);
            if (_gameWindow == IntPtr.Zero)
            {
                throw new CaptureException($"未找到游戏进程 {WindowFinder.GameProcessName}，请先启动《洛克王国：世界》客户端");
            }

            // 主动激活游戏窗口，免去手动右键聚焦（Unreal 引擎左键被游戏输入层截获，不触发 OS 窗口激活）
            WindowFinder.ActivateWindow(_gameWindow);

            if (_spec.ExistingSource is not null)
            {
                _source = _spec.ExistingSource;
                _ownsSource = false;
            }
            else
            {
                _source = await _factories.Capture(new CaptureOptions
                {
                    WindowTitleSubstring = "洛克王国",
                    Backend = CaptureBackendMode.ForceWgcWindow,
                }, cancellationToken);
                _ownsSource = true;
            }

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
            // 票 13-D：鼠标加速开启时 ppc 标定追的是非线性靶，告警但不阻塞
            if (MouseAccelerationProbe.IsEnabled())
            {
                _bus.Emit("warning", new Dictionary<string, object?>
                {
                    ["reason"] = "mouse_acceleration_on",
                });
            }

            _recorder?.AttachBus(_bus);
        })
    {
        Remedy = ex => ex is CaptureException
            ? "把《洛克王国：世界》客户端开起来（窗口标题含「洛克王国」）再重试"
            : "把镜头转向一只野外精灵再重试",
    };

    private ArmingStep CalibrationStep() => new(
        "calibration",
        "灵敏度校准：向各轴发探针并量画面位移…",
        async cancellationToken =>
        {
            // 校准失败（null）不阻断启动，回退到手动灵敏度设置
            var result = await Task.Run(
                () => AutoCalibrator.Calibrate(_source!, _driver!), cancellationToken);
            _calibratedPpc = result;
        })
    {
        Remedy = _ => "校准失败不影响运行，可在配置中关闭「投掷前灵敏度校准」跳过此步",
    };

    private ArmingStep EngineStep() => new(
        "engine",
        "正在初始化投掷循环…",
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loopOptions = _spec.Loop;
            if (_calibratedPpc is { } cal)
            {
                loopOptions = loopOptions with { PpcX = cal.PpcX, PpcY = cal.PpcY };
            }

            _engine = new CatchLoopEngine(
                loopOptions, _spec.Mode, _sensor!, _driver!, _controller!, _bus!, inputGate: InputGate);
            return Task.CompletedTask;
        });

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
