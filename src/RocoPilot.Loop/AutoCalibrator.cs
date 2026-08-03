using RocoPilot.Capture;
using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed class AutoCalibrator
{
    private const int ProbeCounts = 120;
    private const int SettleMs = 400;
    private const int ProbeRounds = 3;

    public sealed record CalibrationResult(double PpcX, double PpcY);

    public static CalibrationResult? Calibrate(
        ICaptureSource capture,
        IInputDriver driver,
        Action<int>? sleepMs = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(driver);
        var sleep = sleepMs ?? (ms => Thread.Sleep(ms));

        var ppcX = CalibrateAxis(capture, driver, sleep, horizontal: true);
        var ppcY = CalibrateAxis(capture, driver, sleep, horizontal: false);

        if (ppcX is null && ppcY is null) return null;

        return new CalibrationResult(ppcX ?? 0, ppcY ?? 0);
    }

    private static double? CalibrateAxis(
        ICaptureSource capture, IInputDriver driver,
        Action<int> sleep, bool horizontal)
    {
        var samples = new List<double>();

        for (var round = 0; round < ProbeRounds; round++)
        {
            if (!capture.TryGrabLatest(out var frameA) || frameA is null) return null;
            var pixelsA = frameA.Pixels.ToArray();
            var w = frameA.Width;
            var h = frameA.Height;
            frameA.Dispose();

            if (horizontal)
                driver.MoveRelative(ProbeCounts, 0);
            else
                driver.MoveRelative(0, ProbeCounts);
            sleep(SettleMs);

            if (!capture.TryGrabLatest(out var frameB) || frameB is null) return null;
            var pixelsB = frameB.Pixels.ToArray();
            frameB.Dispose();

            var shift = SceneShiftEstimator.Estimate(pixelsA, pixelsB, w, h);
            if (shift is not { } s) continue;

            var displacement = horizontal ? Math.Abs(s.Dx) : Math.Abs(s.Dy);
            var ppc = displacement / ProbeCounts;
            if (ppc > 0.01 && ppc < 50)
            {
                samples.Add(ppc);
            }

            if (horizontal)
                driver.MoveRelative(-ProbeCounts, 0);
            else
                driver.MoveRelative(0, -ProbeCounts);
            sleep(SettleMs);
        }

        if (samples.Count == 0) return null;

        return SensitivityCalibrator.Median(samples);
    }
}
