using RocoPilot.Detection;

namespace RocoPilot.Loop;

public interface ICenteringSensor
{
    IReadOnlyList<StableTarget> ObserveStable();

    (int Width, int Height) LatestFrameSize { get; }

    /// <summary>暂停帧处理（镜头转动前调用，阻止过渡帧污染 track）。</summary>
    void SuspendSensing();

    /// <summary>恢复帧处理。</summary>
    void ResumeSensing();

    /// <summary>清空 StabilityGate track 历史与快照（镜头稳定后、恢复前调用）。</summary>
    void ResetStability();
}
