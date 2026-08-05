namespace RocoPilot.Routing;

public sealed class RoutePlaybackSettings
{
    public const string ToolId = "route-playback";

    public int StuckThresholdMs { get; set; } = 5000;

    public int StrokeJitterMaxMs { get; set; } = 15;

    public int CumulativeJitterCapMs { get; set; } = 250;

    public double StillFrameMeanDiff { get; set; } = 2.0;

    public int SegmentPauseMinMs { get; set; } = 1500;

    public int SegmentPauseMaxMs { get; set; } = 4000;

    public void SanitizeInPlace()
    {
        StuckThresholdMs = (int)Math.Clamp(StuckThresholdMs, 500, 60_000);
        StrokeJitterMaxMs = (int)Math.Clamp(StrokeJitterMaxMs, 0, 200);
        CumulativeJitterCapMs = (int)Math.Clamp(CumulativeJitterCapMs, 0, 2000);
        StillFrameMeanDiff = Math.Clamp(StillFrameMeanDiff, 0.1, 50.0);
        SegmentPauseMinMs = (int)Math.Clamp(SegmentPauseMinMs, 0, 60_000);
        SegmentPauseMaxMs = (int)Math.Clamp(SegmentPauseMaxMs, SegmentPauseMinMs, 60_000);
    }

    public RoutePlayerOptions ToPlayerOptions() => new()
    {
        StuckThreshold = TimeSpan.FromMilliseconds(StuckThresholdMs),
        StrokeJitterMax = TimeSpan.FromMilliseconds(StrokeJitterMaxMs),
        CumulativeJitterCap = TimeSpan.FromMilliseconds(CumulativeJitterCapMs),
        StillFrameMeanDiff = StillFrameMeanDiff,
    };

    public GraphExecutorOptions ToExecutorOptions() => new()
    {
        SegmentPauseMin = TimeSpan.FromMilliseconds(SegmentPauseMinMs),
        SegmentPauseMax = TimeSpan.FromMilliseconds(SegmentPauseMaxMs),
    };
}
