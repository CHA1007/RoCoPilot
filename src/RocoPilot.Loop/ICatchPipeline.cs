using RocoPilot.Detection;

namespace RocoPilot.Loop;

public interface ICatchPipeline : IDisposable
{
    IReadOnlyList<ArmingStep> ArmingSteps { get; }

    Func<bool> InputGate { get; }

    IntPtr GameWindow { get; }

    CatchEventBus Bus { get; }

    /// <summary>当前稳定目标快照（调试叠层用）。</summary>
    IReadOnlyList<StableTarget> ObserveDetections();

    /// <summary>检测帧尺寸（坐标映射用）。</summary>
    (int Width, int Height) SensorFrameSize { get; }

    /// <summary>当前投掷目标 TrackId（-1＝无），调试叠层换色用。</summary>
    int ActiveTrackId { get; }

    void Run(CancellationToken cancellationToken);

    bool Pause(string source = "manual");

    bool Resume(string source = "manual");

    void SetSensing(bool enabled);
}
