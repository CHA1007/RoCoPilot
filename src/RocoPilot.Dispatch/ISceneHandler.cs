namespace RocoPilot.Dispatch;

public interface ISceneHandler
{
    GameScene Scene { get; }

    bool IsEnabled { get; }

    void Activate(SceneContext context);

    bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height);

    void Deactivate();
}
