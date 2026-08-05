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

    public bool UseGpu { get; init; }

    public int DetectionIntervalMs { get; init; }

    public TimeSpan DeviceDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string? SessionLogDirectory { get; init; }

    public ICaptureSource? ExistingSource { get; init; }

    public bool CalibrateBeforeThrow { get; init; } = false;

    public Action<double, double>? OnCalibrated { get; init; }
}

public sealed record CatchPipelineFactories
{
    public Func<DetectionOptions, bool, IDetector> Detector { get; init; } =
        (options, useGpu) => DetectorFactory.CreateOnnxYolo(options, useGpu: useGpu);

    public Func<CaptureOptions, CancellationToken, Task<ICaptureSource>> Capture { get; init; } =
        CaptureSourceFactory.StartBestAvailableAsync;

    public Func<IInputDriver> Driver { get; init; } = InputDriverFactory.Create;

    public Func<bool> IsGameForeground { get; init; } = () => global::RocoPilot.Capture.WindowFinder.IsForegroundProcess(global::RocoPilot.Capture.WindowFinder.GameProcessName);

    public Func<ISceneImageEncoder> SceneImageEncoder { get; init; } = () => new WpfSceneImageEncoder();
}
