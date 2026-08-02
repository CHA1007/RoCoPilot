namespace RocoPilot.Loop;

internal static class LoopGuards
{
    internal static void RejectNonFinite(double value, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new LoopException($"{what}须为有限数，实得 {value}");
    }

    internal static (double X, double Y) ScreenCenter(ICenteringSensor sensor)
    {
        var (width, height) = sensor.LatestFrameSize;
        if (width <= 0 || height <= 0)
            throw new LoopException("须有捕获帧（帧尺寸为 0：捕获未起或未出帧）");
        return (width / 2.0, height / 2.0);
    }

    internal static bool WithinTolerance(double offsetX, double offsetY, double tolerancePx) =>
        Math.Abs(offsetX) <= tolerancePx && Math.Abs(offsetY) <= tolerancePx;
}
