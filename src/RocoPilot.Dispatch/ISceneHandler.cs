namespace RocoPilot.Dispatch;

public interface ISceneHandler
{
    GameScene Scene { get; }

    bool IsEnabled { get; }

    void Activate(SceneContext context);

    bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height);

    void Deactivate();

    public virtual void SuspendSensing() { }

    public virtual void ResumeSensing() { }

    public virtual bool HoldActivation(GameScene nextScene) => false;

    public virtual bool PauseOnFocusLost() => false;

    public virtual void ResumeAfterFocusRestored() { }
}
