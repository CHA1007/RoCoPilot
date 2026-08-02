namespace RocoPilot.Dispatch;

/// <summary>场景处理器：负责某一场景下的具体功能逻辑。</summary>
public interface ISceneHandler
{
    /// <summary>该处理器负责的场景。</summary>
    GameScene Scene { get; }

    /// <summary>用户是否已启用此功能。</summary>
    bool IsEnabled { get; }

    /// <summary>场景切入时调用（轻量初始化）。</summary>
    void Activate(SceneContext context);

    /// <summary>每帧调用（主逻辑）。返回 true 表示已处理，false 表示无事可做。</summary>
    bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height);

    /// <summary>场景切出时调用（停止输入、清理状态）。</summary>
    void Deactivate();
}
