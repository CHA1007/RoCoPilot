using RocoPilot.Detection;
using RocoPilot.Loop;

namespace RocoPilot.Tools.AutoThrow;

public static class AutoThrowMappings
{
    public static DetectionOptions ToDetectionOptions(this AutoThrowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new DetectionOptions
        {
            ConfidenceThreshold = settings.DetectionConfidence,
            IouThreshold = settings.DetectionIou,
            StableFrames = settings.DetectionStableFrames,
            Whitelist = settings.DetectionWhitelist ?? [],
        };
    }

    public static CenteringOptions ToCenteringOptions(this AutoThrowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CenteringOptions
        {
            TolerancePx = settings.CenterTolerancePx,
            MaxSteps = settings.CenterMaxSteps,
            RecheckMs = settings.CenterRecheckMs,
            MaxStepCounts = settings.CenterMaxStepCounts,
            FallbackDivisor = settings.CenterFallbackDivisor,
            SensitivityPpc = settings.SensitivityPpc,
        };
    }

    public static CatchLoopOptions ToLoopOptions(this AutoThrowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CatchLoopOptions
        {
            ChargeMs = settings.ChargeMs,
            ChargeJitterMs = settings.ChargeJitterMs,
            PostSettleDelayMinMs = settings.PostSettleDelayMinMs,
            PostSettleDelayMaxMs = settings.PostSettleDelayMaxMs,
            AimJitterPx = settings.AimJitterPx,
            CommandNoiseCounts = settings.CommandNoiseCounts,
        };
    }

    public static CatchPipelineSpec ToPipelineSpec(this AutoThrowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CatchPipelineSpec
        {
            Detection = settings.ToDetectionOptions(),
            Centering = settings.ToCenteringOptions(),
            Loop = settings.ToLoopOptions(),
            WindowTitleSubstring = string.IsNullOrWhiteSpace(settings.WindowTitleSubstring)
                ? AutoThrowSettings.DefaultWindowTitle
                : settings.WindowTitleSubstring.Trim(),
            InputBackend = settings.InputBackend,
            UseGpu = string.Equals(settings.InferenceDevice, "gpu", StringComparison.OrdinalIgnoreCase),
            DetectionIntervalMs = settings.DetectionIntervalMs,
        };
    }
}
