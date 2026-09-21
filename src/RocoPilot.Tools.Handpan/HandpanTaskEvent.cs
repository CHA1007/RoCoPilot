using RocoPilot.Core;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan;

public abstract record HandpanTaskEvent
{
    public abstract string Name { get; }

    public ToolEvent AsToolEvent() => new(Name) { Payload = this };
}

public sealed record HandpanArranged(
    int NoteCount,
    int MissingCount,
    int Semitones,
    double ExactCoverage,
    double TotalSeconds) : HandpanTaskEvent
{
    public override string Name => "handpan_arranged";
}

public sealed record HandpanCountdown(int SecondsLeft) : HandpanTaskEvent
{
    public override string Name => "handpan_countdown";
}

public sealed record HandpanPaused(PauseSource Source) : HandpanTaskEvent
{
    public override string Name => "handpan_paused";
}

public sealed record HandpanResumed(PauseSource Source) : HandpanTaskEvent
{
    public override string Name => "handpan_resumed";
}

public sealed record HandpanRoundCompleted(int Round) : HandpanTaskEvent
{
    public override string Name => "handpan_round_completed";
}

public sealed record HandpanCompleted : HandpanTaskEvent
{
    public override string Name => "handpan_completed";
}

public sealed record HandpanProbeKey(string Key, string Note) : HandpanTaskEvent
{
    public override string Name => "handpan_probe_key";
}

public sealed record HandpanProbeCompleted(int KeyCount) : HandpanTaskEvent
{
    public override string Name => "handpan_probe_completed";
}

public sealed record HandpanFaulted(string Error, string? Remedy = null) : HandpanTaskEvent
{
    public override string Name => "handpan_faulted";
}
