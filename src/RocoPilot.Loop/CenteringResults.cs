namespace RocoPilot.Loop;

public enum CenteringOutcome
{
    Centered,

    Lost,

    MaxSteps,
}

public sealed record CenteringRequest
{
    public (float X, float Y)? Anchor { get; init; }

    public bool MovesEnabled { get; init; } = true;
}

public sealed record CenteredTargetInfo(string ClassName, float Confidence, float BoxArea, int TrackId);

public sealed record CenteringResult(
    CenteringOutcome Outcome,
    int Steps,
    double ResidualPx,
    (double X, double Y)? InitialOffset,
    (double X, double Y)? FinalOffset,
    CalibrationSource CalibrationSource,
    double? PixelsPerCount,
    TimeSpan Elapsed,
    CenteredTargetInfo? Target,
    IReadOnlyList<(double X, double Y)> StepOffsets);
