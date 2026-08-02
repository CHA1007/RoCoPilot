using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Dispatch;

/// <summary>喂给 <see cref="ISceneHandler"/> 的运行时上下文。</summary>
public sealed class SceneContext
{
    public required IInputDriver InputDriver { get; init; }

    /// <summary>发送事件（经调度器转发到外壳）。</summary>
    public required Action<ToolEvent> EmitEvent { get; init; }

    /// <summary>游戏窗口是否在前台（失焦门控）。</summary>
    public required Func<bool> IsGameForeground { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
