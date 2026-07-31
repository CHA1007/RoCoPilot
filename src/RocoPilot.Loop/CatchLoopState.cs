namespace RocoPilot.Loop;

public enum CatchLoopState
{
    Idle,

    Running,

    Paused,
}

public enum CatchPhase
{
    Scanning,

    Centering,

    Throwing,

    Settling,
}

public enum CatchLoopMode
{
    DryRun,
    MoveOnly,
    Live,
}
