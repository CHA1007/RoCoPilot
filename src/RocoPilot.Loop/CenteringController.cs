using System.Diagnostics;
using RocoPilot.Core;
using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed class CenteringController
{
    private const int GatePollChunkMs = 100;

    private readonly CenteringOptions _options;
    private readonly ICenteringSensor _sensor;
    private readonly IInputDriver _driver;
    private readonly SensitivityCalibrator _calibrator;
    private readonly CalibrationCache _cache;
    private readonly Action<int, CancellationToken> _sleep;
    private readonly Func<bool>? _inputGate;

    public CenteringController(
        CenteringOptions options,
        ICenteringSensor sensor,
        IInputDriver driver,
        CalibrationCache? cache = null,
        SensitivityCalibrator? calibrator = null,
        Action<int, CancellationToken>? sleep = null,
        Func<bool>? inputGate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Normalized();
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _cache = cache ?? new CalibrationCache();
        _sleep = sleep ?? LoopTiming.Sleep;
        _calibrator = calibrator ?? new SensitivityCalibrator(_options, _sleep);
        _inputGate = inputGate;
    }

    public event EventHandler<ToolEvent>? EventRaised;

    public Func<double>? CorrectionNoise { get; set; }

    public CenteringOptions AppliedOptions => _options;

    public CalibrationCache Cache => _cache;

    public SensitivityCalibrator Calibrator => _calibrator;

    public CenteringResult RunOnce(CenteringRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new CenteringRequest();
        WaitWhileGateClosed(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var (screenCenterX, screenCenterY) = ScreenCenter();
        var stepOffsets = new List<(double X, double Y)>();
        var source = CalibrationSource.None;
        double? ppcReport = null;

        var anchor = request.Anchor ?? ((float)screenCenterX, (float)screenCenterY);
        var observation = TargetSelection.Pick(_sensor.ObserveStable(), lockedTrackId: null, anchor.X, anchor.Y);
        if (observation is null)
        {
            return new CenteringResult(
                Outcome: CenteringOutcome.Lost, Steps: 0, ResidualPx: 0,
                InitialOffset: null, FinalOffset: null,
                CalibrationSource: source, PixelsPerCount: null,
                Elapsed: stopwatch.Elapsed, Target: null, StepOffsets: stepOffsets);
        }

        anchor = observation.MedianCenter;
        var lockedTrack = observation.TrackId;
        var target = new CenteredTargetInfo(
            observation.Latest.ClassName, observation.Latest.Confidence, observation.Latest.Area, observation.TrackId);
        var initialOffset = OffsetFromScreenCenter(observation.MedianCenter, screenCenterX, screenCenterY);
        var offset = initialOffset;
        var steps = 0;

        if (WithinTolerance(offset.X, offset.Y))
        {
            source = CalibrationSource.Skipped;
            EmitCentered(0, offset, source, null, stopwatch);
            return Finish(CenteringOutcome.Centered, 0, offset, initialOffset, source, null, target, stepOffsets, stopwatch);
        }

        var calibrated = false;
        if (_cache.HasValue)
        {
            source = CalibrationSource.Cached;
            ppcReport = PickBucketPpc(Hypot(initialOffset.X, initialOffset.Y));
            Emit(new ToolEvent("calibration", new Dictionary<string, object?>
            {
                ["source"] = "cached",
                ["ppc"] = Round3(ppcReport),
            }));
        }
        else if (request.MovesEnabled)
        {
            calibrated = true;
            var calibration = _calibrator.Calibrate(_sensor, _driver, _cache, anchor, lockedTrack, cancellationToken);
            Emit(new ToolEvent("calibration", new Dictionary<string, object?>
            {
                ["source"] = calibration.Source.EventString(),
                ["ppc"] = Round3(calibration.PixelsPerCount),
                ["probes_ok"] = calibration.ProbesSucceeded,
                ["probes_total"] = calibration.ProbesAttempted,
            }));
            if (calibration.FinalAnchor is { } finalAnchor)
            {
                anchor = finalAnchor;
            }

            if (calibration.FinalOffset is { } finalOffset)
            {
                offset = finalOffset;
            }

            source = calibration.Source switch
            {
                CalibrationSource.Fresh => CalibrationSource.Fresh,
                CalibrationSource.Skipped => CalibrationSource.Skipped,
                _ => CalibrationSource.Failed,
            };
            ppcReport = calibration.PixelsPerCount;
        }
        else
        {
            source = CalibrationSource.Skipped;
        }

        var outcome = CenteringOutcome.MaxSteps;
        var current = observation;
        var pendingOnline = false;
        double preMoveCenterX = 0, preMoveCenterY = 0, lastCommandX = 0, lastCommandY = 0;

        if (calibrated)
        {
            var refreshed = TargetSelection.Pick(_sensor.ObserveStable(), lockedTrack, anchor.X, anchor.Y);
            if (refreshed is null)
            {
                EmitFailed("lost", 0, offset, source, ppcReport, stopwatch);
                return Finish(CenteringOutcome.Lost, 0, offset, initialOffset, source, ppcReport, target, stepOffsets, stopwatch);
            }

            current = refreshed;
        }

        while (steps < _options.MaxSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitWhileGateClosed(cancellationToken);

            anchor = current.MedianCenter;
            offset = OffsetFromScreenCenter(current.MedianCenter, screenCenterX, screenCenterY);
            steps++;
            stepOffsets.Add(offset);

            if (WithinTolerance(offset.X, offset.Y))
            {
                outcome = CenteringOutcome.Centered;
                break;
            }

            if (pendingOnline)
            {
                var movedX = (double)current.MedianCenter.X - preMoveCenterX;
                var movedY = (double)current.MedianCenter.Y - preMoveCenterY;
                var useX = Math.Abs(lastCommandX) >= Math.Abs(lastCommandY);
                if (_calibrator.TryUpdateOnline(_cache, useX ? lastCommandX : lastCommandY, useX ? movedX : movedY) is { } update)
                {
                    ppcReport = update.PixelsPerCount;
                    if (update.Seeded || update.Significant)
                    {
                        source = CalibrationSource.Online;
                        Emit(new ToolEvent("calibration", new Dictionary<string, object?>
                        {
                            ["source"] = "online",
                            ["ppc"] = Round3(update.PixelsPerCount),
                        }));
                    }
                }
            }

            var bucketPpc = PickBucketPpc(Hypot(offset.X, offset.Y));
            var countsPerPx = bucketPpc is { } ppc
                ? 1.0 / ppc
                : 1.0 / _options.FallbackDivisor;
            var commandX = offset.X * countsPerPx;
            var commandY = offset.Y * countsPerPx;
            var commandMagnitude = Hypot(commandX, commandY);
            if (commandMagnitude > _options.MaxStepCounts)
            {
                var scale = _options.MaxStepCounts / commandMagnitude;
                commandX *= scale;
                commandY *= scale;
            }

            preMoveCenterX = current.MedianCenter.X;
            preMoveCenterY = current.MedianCenter.Y;
            lastCommandX = commandX;
            lastCommandY = commandY;

            if (request.MovesEnabled)
            {
                var moveX = commandX;
                var moveY = commandY;
                if (CorrectionNoise is { } noise)
                {
                    moveX += noise();
                    moveY += noise();
                }

                _driver.MoveRelative((int)Math.Round(moveX), (int)Math.Round(moveY));
            }

            _sleep(_options.RecheckMs, cancellationToken);
            pendingOnline = request.MovesEnabled;

            var next = TargetSelection.Pick(_sensor.ObserveStable(), lockedTrack, anchor.X, anchor.Y);
            if (next is null)
            {
                outcome = CenteringOutcome.Lost;
                break;
            }

            current = next;
        }

        if (outcome == CenteringOutcome.MaxSteps && request.MovesEnabled)
        {
            _cache.Invalidate();
        }

        if (outcome == CenteringOutcome.Centered)
        {
            EmitCentered(steps, offset, source, ppcReport, stopwatch);
        }
        else
        {
            EmitFailed(outcome == CenteringOutcome.Lost ? "lost" : "max_steps", steps, offset, source, ppcReport, stopwatch);
        }

        return Finish(outcome, steps, offset, initialOffset, source, ppcReport, target, stepOffsets, stopwatch);
    }

    private void WaitWhileGateClosed(CancellationToken cancellationToken)
    {
        while (_inputGate?.Invoke() is false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sleep(GatePollChunkMs, cancellationToken);
        }
    }

    private void EmitCentered(
        int steps, (double X, double Y) offset, CalibrationSource source, double? ppc, Stopwatch stopwatch) =>
        Emit(new ToolEvent("centered", new Dictionary<string, object?>
        {
            ["residual_px"] = Math.Round(Hypot(offset.X, offset.Y), 1),
            ["steps"] = steps,
            ["elapsed_ms"] = (int)stopwatch.ElapsedMilliseconds,
            ["calib"] = source.EventString(),
            ["ppc"] = Round3(ppc),
        }));

    private void EmitFailed(
        string reason, int steps, (double X, double Y) offset, CalibrationSource source, double? ppc, Stopwatch stopwatch) =>
        Emit(new ToolEvent("centering_failed", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["steps"] = steps,
            ["elapsed_ms"] = (int)stopwatch.ElapsedMilliseconds,
            ["calib"] = source.EventString(),
            ["ppc"] = Round3(ppc),
            ["residual_px"] = Math.Round(Hypot(offset.X, offset.Y), 1),
        }));

    private void Emit(ToolEvent toolEvent) => EventRaised?.Invoke(this, toolEvent);

    private CenteringResult Finish(
        CenteringOutcome outcome, int steps, (double X, double Y) finalOffset, (double X, double Y) initialOffset,
        CalibrationSource source, double? ppc, CenteredTargetInfo target,
        List<(double X, double Y)> stepOffsets, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new CenteringResult(outcome, steps, Hypot(finalOffset.X, finalOffset.Y), initialOffset, finalOffset,
            source, ppc, stopwatch.Elapsed, target, stepOffsets);
    }

    private bool WithinTolerance(double offsetX, double offsetY) =>
        Math.Abs(offsetX) <= _options.TolerancePx && Math.Abs(offsetY) <= _options.TolerancePx;

    private (double X, double Y) ScreenCenter()
    {
        var (width, height) = _sensor.LatestFrameSize;
        if (width <= 0 || height <= 0)
        {
            throw new LoopException("居中前须有捕获帧（帧尺寸为 0：捕获未起或未出帧）");
        }

        return (width / 2.0, height / 2.0);
    }

    private static (double X, double Y) OffsetFromScreenCenter((float X, float Y) center, double screenCenterX, double screenCenterY) =>
        ((double)center.X - screenCenterX, (double)center.Y - screenCenterY);

    private double? PickBucketPpc(double offsetMagnitude)
    {
        var first = _cache.PpcFor(offsetMagnitude);
        return first is { } guess ? (_cache.PpcFor(offsetMagnitude / guess) ?? first) : null;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double? Round3(double? value) => value is { } v ? Math.Round(v, 3) : null;
}
