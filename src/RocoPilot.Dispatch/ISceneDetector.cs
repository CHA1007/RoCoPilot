namespace RocoPilot.Dispatch;

/// <summary>场景检测器：对当前帧投票，返回认为的场景及置信度。</summary>
public interface ISceneDetector
{
    /// <summary>该检测器负责识别的场景。</summary>
    GameScene Scene { get; }

    /// <summary>
    /// 对 BGRA 帧做检测。
    /// 返回置信度 [0,1]；低于阈值视为未检出。
    /// </summary>
    float Detect(ReadOnlySpan<byte> bgraPixels, int width, int height);
}
