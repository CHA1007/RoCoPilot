using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed record CalibrationResult(
    CalibrationSource Source,
    double? PixelsPerCount,
    (float X, float Y)? FinalAnchor,
    (double X, double Y)? FinalOffset,
    int ProbesSucceeded,
    int ProbesAttempted);

public sealed class SensitivityCalibrator
{
    private readonly CenteringOptions _options;
    private readonly Action<int, CancellationToken> _sleep;

    public SensitivityCalibrator(CenteringOptions options, Action<int, CancellationToken>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Normalized();
        _sleep = sleep ?? LoopTiming.Sleep;
    }

    public CenteringOptions AppliedOptions => _options;

    public CalibrationResult Calibrate(
        ICenteringSensor sensor,
        IInputDriver driver,
        CalibrationCache cache,
        (float X, float Y) anchor,
        int? lockedTrackId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sensor);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(cache);

        var (centerX, centerY) = LoopGuards.ScreenCenter(sensor);
        var attempted = 0;
        var succeeded = 0;

        var target = TargetSelection.Pick(sensor.ObserveStable(), lockedTrackId, anchor.X, anchor.Y);
        if (target is null)
        {
            return new CalibrationResult(CalibrationSource.Failed, null, null, null, 0, 0);
        }

        anchor = target.MedianCenter;
        var offset = (X: (double)target.MedianCenter.X - centerX, Y: (double)target.MedianCenter.Y - centerY);
        if (LoopGuards.WithinTolerance(offset.X, offset.Y, _options.TolerancePx))
        {
            return new CalibrationResult(CalibrationSource.Skipped, null, anchor, offset, 0, 0);
        }

        if (_options.WarmupCounts > 0)
        {
            var (dirX, dirY) = DirectionTowardTarget(offset);
            cancellationToken.ThrowIfCancellationRequested();
            driver.MoveRelative((int)Math.Round(_options.WarmupCounts * dirX), (int)Math.Round(_options.WarmupCounts * dirY));
            _sleep(_options.WarmupSettleMs, cancellationToken);

            target = TargetSelection.Pick(sensor.ObserveStable(), lockedTrackId, anchor.X, anchor.Y);
            if (target is null)
            {
                return new CalibrationResult(CalibrationSource.Failed, null, null, null, succeeded, attempted);
            }

            anchor = target.MedianCenter;
            offset = (X: (double)target.MedianCenter.X - centerX, Y: (double)target.MedianCenter.Y - centerY);
        }

        double? largestBucketPpc = null;
        foreach (var magnitude in _options.ProbeMagnitudes)
        {
            if (LoopGuards.WithinTolerance(offset.X, offset.Y, _options.TolerancePx))
            {
                break;
            }

            var samples = new List<double>(_options.ProbesPerMagnitude);
            for (var i = 0; i < _options.ProbesPerMagnitude; i++)
            {
                var (dirX, dirY) = DirectionTowardTarget(offset);
                var axis = Math.Abs(offset.X) >= Math.Abs(offset.Y);
                var before = axis ? target!.MedianCenter.X : target!.MedianCenter.Y;

                attempted++;
                cancellationToken.ThrowIfCancellationRequested();
                driver.MoveRelative((int)Math.Round(magnitude * dirX), (int)Math.Round(magnitude * dirY));
                _sleep(_options.ProbeSettleMs, cancellationToken);

                target = TargetSelection.Pick(sensor.ObserveStable(), lockedTrackId, anchor.X, anchor.Y);
                if (target is null)
                {
                    return new CalibrationResult(
                        succeeded > 0 ? CalibrationSource.Fresh : CalibrationSource.Failed,
                        largestBucketPpc, null, null, succeeded, attempted);
                }

                anchor = target.MedianCenter;
                offset = (X: (double)target.MedianCenter.X - centerX, Y: (double)target.MedianCenter.Y - centerY);

                var after = axis ? target.MedianCenter.X : target.MedianCenter.Y;
                var moved = (double)after - before;
                var command = magnitude * (axis ? dirX : dirY);
                if (Math.Abs(moved) < _options.MinMeasuredDisplacementPx)
                {
                    continue;
                }

                if (moved * command >= 0)
                {
                    continue;
                }

                succeeded++;
                samples.Add(Math.Clamp(Math.Abs(moved) / magnitude, _options.MinPpc, _options.MaxPpc));
            }

            if (samples.Count == 0)
            {
                continue;
            }

            var bucketPpc = Median(samples);
            cache.Store(magnitude, bucketPpc);
            largestBucketPpc = bucketPpc;
        }

        return largestBucketPpc is { } ppc
            ? new CalibrationResult(CalibrationSource.Fresh, ppc, anchor, offset, succeeded, attempted)
            : LoopGuards.WithinTolerance(offset.X, offset.Y, _options.TolerancePx)
                ? new CalibrationResult(CalibrationSource.Skipped, null, anchor, offset, succeeded, attempted)
                : new CalibrationResult(CalibrationSource.Failed, null, anchor, offset, succeeded, attempted);
    }

    public (bool Seeded, bool Significant, double PixelsPerCount)? TryUpdateOnline(
        CalibrationCache cache, double commandCounts, double movedPx)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (Math.Abs(commandCounts) < _options.OnlineMinCommandCounts)
        {
            return null;
        }

        if (Math.Abs(movedPx) < _options.OnlineMinMovedPx)
        {
            return null;
        }

        if (movedPx * commandCounts >= 0)
        {
            return null;
        }

        var observed = Math.Clamp(Math.Abs(movedPx) / Math.Abs(commandCounts), _options.MinPpc, _options.MaxPpc);
        return cache.ApplyOnlineObservation(commandCounts, observed, _options.OnlineEmaWeight, _options.OnlineRelativeChangeThreshold);
    }

    private static (double DirX, double DirY) DirectionTowardTarget((double X, double Y) offset) =>
        Math.Abs(offset.X) >= Math.Abs(offset.Y)
            ? (offset.X >= 0 ? 1.0 : -1.0, 0.0)
            : (0.0, offset.Y >= 0 ? 1.0 : -1.0);

    internal static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
