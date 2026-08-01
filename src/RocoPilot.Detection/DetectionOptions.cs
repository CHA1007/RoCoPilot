namespace RocoPilot.Detection;

public sealed record DetectionOptions
{
    public double ConfidenceThreshold { get; init; } = 0.10;

    public double IouThreshold { get; init; } =0.7;

    public int MaxBoxes { get; init; } = 300;

    public int StableFrames { get; init; } = 4;

    public double StabilitySpreadPx { get; init; } = 300;

    public double AssociationRadiusPx { get; init; } = 300;

    public IReadOnlyList<string> Whitelist { get; init; } = [];

    internal DetectionOptions Normalized()
    {
        RejectNonFinite(ConfidenceThreshold, "置信阈");
        RejectNonFinite(IouThreshold, "IoU 阈");
        RejectNonFinite(StabilitySpreadPx, "稳定散布容差");
        RejectNonFinite(AssociationRadiusPx, "关联半径");

        return this with
        {
            ConfidenceThreshold = Math.Clamp(ConfidenceThreshold, 1e-6, 1.0),
            IouThreshold = Math.Clamp(IouThreshold, 1e-6, 1.0),
            MaxBoxes = MaxBoxes > 0
                ? MaxBoxes
                : throw new DetectionException($"截顶框数须为正，实得 {MaxBoxes}"),
            StableFrames = Math.Max(1, StableFrames),
            StabilitySpreadPx = StabilitySpreadPx > 0
                ? StabilitySpreadPx
                : throw new DetectionException($"稳定散布容差须为正，实得 {StabilitySpreadPx}"),
            AssociationRadiusPx = AssociationRadiusPx > 0
                ? AssociationRadiusPx
                : throw new DetectionException($"关联半径须为正，实得 {AssociationRadiusPx}"),
            Whitelist = Whitelist ?? [],
        };
    }

    private static void RejectNonFinite(double value, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new DetectionException($"{what}须为有限数，实得 {value}");
    }
}
