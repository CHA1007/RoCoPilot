using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowSettings
{
    public const string DefaultWindowTitle = "洛克王国";

    private const int MaxDelayMs = 3_600_000;

    private static readonly AutoThrowSettings s_baseline = new();

    public string FarmingSpotName { get; set; } = "眠枭庇护所";

    public double DetectionConfidence { get; set; } = 0.45;

    public double DetectionIou { get; set; } = 0.75;

    public int DetectionStableFrames { get; set; } = 4;

    public string[] DetectionWhitelist { get; set; } = [];

    public double CenterTolerancePx { get; set; } = 20;

    public int CenterMaxSteps { get; set; } = 6;

    public int CenterRecheckMs { get; set; } = 150;

    public int CenterMaxStepCounts { get; set; } = 250;

    public double CenterFallbackDivisor { get; set; } = 4;

    public double SensitivityPpc { get; set; }

    public int ChargeMs { get; set; } = 200;

    public int ChargeJitterMs { get; set; }

    public int PostSettleDelayMinMs { get; set; } = 100;

    public int PostSettleDelayMaxMs { get; set; } = 100;

    public double AimJitterPx { get; set; }

    public double CommandNoiseCounts { get; set; }

    public string InputBackend { get; set; } = InputDriverFactory.Interception;

    public string InferenceDevice { get; set; } = "cpu";

    public int DetectionIntervalMs { get; set; } = 200;

    public string WindowTitleSubstring { get; set; } = DefaultWindowTitle;

    public bool CalibrateBeforeThrow { get; set; } = true;

    public void SanitizeInPlace()
    {
        FarmingSpotName = (FarmingSpotName ?? string.Empty).Trim();
        DetectionWhitelist = SanitizeWhitelist(DetectionWhitelist);
        InputBackend = SanitizeBackend(InputBackend);
        InferenceDevice = string.Equals(InferenceDevice, "gpu", StringComparison.OrdinalIgnoreCase) ? "gpu" : "cpu";
        DetectionIntervalMs = (int)Clamp(DetectionIntervalMs, 0, 5000);
        WindowTitleSubstring = SanitizeWindowTitle(WindowTitleSubstring);

        DetectionConfidence = FiniteOrFallback(DetectionConfidence, s_baseline.DetectionConfidence, 0, 1);
        DetectionIou = FiniteOrFallback(DetectionIou, s_baseline.DetectionIou, 0, 1);
        DetectionStableFrames = (int)Clamp(DetectionStableFrames, 2, 20);
        CenterTolerancePx = FiniteOrFallback(CenterTolerancePx, s_baseline.CenterTolerancePx, 1, 200);
        CenterMaxSteps = (int)Clamp(CenterMaxSteps, 1, 20);
        CenterRecheckMs = (int)Clamp(CenterRecheckMs, 100, 1000);
        CenterMaxStepCounts = (int)Clamp(CenterMaxStepCounts, 10, 1000);
        CenterFallbackDivisor = FiniteOrFallback(CenterFallbackDivisor, s_baseline.CenterFallbackDivisor, 1, 50);
        SensitivityPpc = FiniteOrFallback(SensitivityPpc, 0, 0, 100);
        ChargeMs = (int)Clamp(ChargeMs, 10, 3000);
        ChargeJitterMs = (int)Math.Min(Clamp(ChargeJitterMs, 0, 400), ChargeMs - 1);
        PostSettleDelayMinMs = (int)Clamp(PostSettleDelayMinMs, 100, MaxDelayMs);
        PostSettleDelayMaxMs = (int)Clamp(PostSettleDelayMaxMs, 100, MaxDelayMs);
        if (PostSettleDelayMaxMs < PostSettleDelayMinMs)
        {
            PostSettleDelayMaxMs = PostSettleDelayMinMs;
        }

        AimJitterPx = FiniteOrFallback(AimJitterPx, s_baseline.AimJitterPx, 0, 50);
        CommandNoiseCounts = FiniteOrFallback(CommandNoiseCounts, s_baseline.CommandNoiseCounts, 0, 20);
    }

    private static double FiniteOrFallback(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    private static string SanitizeBackend(string? backend)
    {
        var normalized = (backend ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is InputDriverFactory.Interception or InputDriverFactory.SendInput
            ? normalized
            : InputDriverFactory.Interception;
    }

    private static string SanitizeWindowTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        return trimmed.Length == 0 ? DefaultWindowTitle : trimmed;
    }

    private static string[] SanitizeWhitelist(string[]? whitelist)
    {
        if (whitelist is not { Length: > 0 })
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(whitelist.Length);
        foreach (var entry in whitelist)
        {
            var trimmed = (entry ?? string.Empty).Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return [.. result];
    }
}
