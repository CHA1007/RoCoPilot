using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Dispatch;

public sealed class SceneContext
{
    public required IInputDriver InputDriver { get; init; }

    public required Action<ToolEvent> EmitEvent { get; init; }

    public required Func<bool> IsGameForeground { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
