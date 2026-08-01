using RocoPilot.Capture;
using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed record CatchPipelineSpec
{
    public DetectionOptions Detection { get; init; } = new();

    public CenteringOptions Centering { get; init; } = new();

    public CatchLoopOptions Loop { get; init; } = new();

    public CatchLoopMode Mode { get; init; } = CatchLoopMode.Live;

    public string? WindowTitleSubstring { get; init; } = "洛克王国";

    public string? InputBackend { get; init; }

    public bool UseGpu { get; init; }

    public int DetectionIntervalMs { get; init; }

    public TimeSpan DeviceDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan FirstStableTargetTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public string? SessionLogDirectory { get; init; }

    /// <summary>外部提供的截图源（如 CaptureHost）。非空时 CaptureStep 直接复用，不另建；Dispose 不释放。</summary>
    public ICaptureSource? ExistingSource { get; init; }

    /// <summary>投掷前是否执行场景位移法灵敏度校准。</summary>
    public bool CalibrateBeforeThrow { get; init; } = true;
}

public sealed record CatchPipelineFactories
{
    public Func<DetectionOptions, bool, IDetector> Detector { get; init; } =
        (options, useGpu) => DetectorFactory.CreateOnnxYolo(options, useGpu: useGpu);

    public Func<CaptureOptions, CancellationToken, Task<ICaptureSource>> Capture { get; init; } =
        CaptureSourceFactory.StartBestAvailableAsync;

    public Func<string?, IInputDriver> Driver { get; init; } = InputDriverFactory.Create;

    public Func<string?, IntPtr> WindowFinder { get; init; } = global::RocoPilot.Capture.WindowFinder.FindFirstByTitleSubstring;

    public Func<bool> IsGameForeground { get; init; } = () => global::RocoPilot.Capture.WindowFinder.IsForegroundProcess(global::RocoPilot.Capture.WindowFinder.GameProcessName);

    public Func<ISceneImageEncoder> SceneImageEncoder { get; init; } = () => new WpfSceneImageEncoder();
}
