using RocoPilot.Detection;

namespace RocoPilot.Loop;

public interface ICatchPipeline : IDisposable
{
    IReadOnlyList<ArmingStep> ArmingSteps { get; }

    Func<bool> InputGate { get; }

    IntPtr GameWindow { get; }

    CatchEventBus Bus { get; }

    IReadOnlyList<StableTarget> ObserveDetections();

    (int Width, int Height) SensorFrameSize { get; }

    int ActiveTrackId { get; }

    void Run(CancellationToken cancellationToken);

    bool Pause(string source = "manual");

    bool Resume(string source = "manual");

    void SetSensing(bool enabled);
}
