namespace RocoPilot.Dispatch;

public interface ISceneDetector
{
    GameScene Scene { get; }

    float Detect(ReadOnlySpan<byte> bgraPixels, int width, int height);
}
