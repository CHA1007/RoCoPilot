using RocoPilot.Detection;

namespace RocoPilot.Loop;

public interface ICenteringSensor
{
    IReadOnlyList<StableTarget> ObserveStable();

    (int Width, int Height) LatestFrameSize { get; }
}
