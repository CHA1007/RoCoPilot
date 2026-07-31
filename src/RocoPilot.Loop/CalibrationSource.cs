namespace RocoPilot.Loop;

public enum CalibrationSource
{
    None,

    Cached,

    Fresh,

    Online,

    Failed,

    Skipped,
}

public static class CalibrationSourceExtensions
{
    public static string EventString(this CalibrationSource source) => source switch
    {
        CalibrationSource.Cached => "cached",
        CalibrationSource.Fresh => "fresh",
        CalibrationSource.Online => "online",
        CalibrationSource.Failed => "failed",
        CalibrationSource.Skipped => "skip",
        _ => "none",
    };
}
