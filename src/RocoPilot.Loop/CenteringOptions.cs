namespace RocoPilot.Loop;

public sealed record CenteringOptions
{
    public double TolerancePx { get; init; } = 20;

    public int MaxSteps { get; init; } = 6;

    public int RecheckMs { get; init; } = 150;

    public int MaxStepCounts { get; init; } = 250;

    public double FallbackDivisor { get; init; } = 4;

    public double SensitivityPpc { get; init; }

    public int WarmupCounts { get; init; } = 90;

    public int WarmupSettleMs { get; init; } = 600;

    public IReadOnlyList<int> ProbeMagnitudes { get; init; } = [60, 160];

    public int ProbesPerMagnitude { get; init; } = 3;

    public int ProbeSettleMs { get; init; } = 600;

    public double MinMeasuredDisplacementPx { get; init; } = 12;

    public double MinPpc { get; init; } = 0.02;

    public double MaxPpc { get; init; } = 5.0;

    public double OnlineEmaWeight { get; init; } = 0.3;

    public double OnlineMinCommandCounts { get; init; } = 40;

    public double OnlineMinMovedPx { get; init; } = 8;

    public double OnlineRelativeChangeThreshold { get; init; } = 0.05;

    public double Gain { get; init; } = 0.6;

    public int ChunkThreshold { get; init; } = 80;

    public int ChunkDelayMs { get; init; } = 10;

    internal CenteringOptions Normalized()
    {
        LoopGuards.RejectNonFinite(TolerancePx, "居中容差");
        LoopGuards.RejectNonFinite(FallbackDivisor, "回退除数");
        LoopGuards.RejectNonFinite(SensitivityPpc, "内置灵敏度");
        LoopGuards.RejectNonFinite(MinMeasuredDisplacementPx, "最小实测位移");
        LoopGuards.RejectNonFinite(MinPpc, "ppc 下限");
        LoopGuards.RejectNonFinite(MaxPpc, "ppc 上限");
        LoopGuards.RejectNonFinite(OnlineEmaWeight, "在线校正权重");
        LoopGuards.RejectNonFinite(OnlineMinCommandCounts, "在线校正指令门槛");
        LoopGuards.RejectNonFinite(OnlineMinMovedPx, "在线校正位移门槛");
        LoopGuards.RejectNonFinite(OnlineRelativeChangeThreshold, "在线校正变化门槛");

        if (TolerancePx <= 0) throw new LoopException($"居中容差须为正，实得 {TolerancePx}");
        if (MaxSteps <= 0) throw new LoopException($"最大步数须为正，实得 {MaxSteps}");
        if (RecheckMs <= 0) throw new LoopException($"重检 tick 须为正，实得 {RecheckMs}");
        if (MaxStepCounts <= 0) throw new LoopException($"单步封顶须为正，实得 {MaxStepCounts}");
        if (FallbackDivisor <= 0) throw new LoopException($"回退除数须为正，实得 {FallbackDivisor}");
        if (SensitivityPpc < 0) throw new LoopException($"内置灵敏度不可为负，实得 {SensitivityPpc}");
        if (WarmupCounts < 0) throw new LoopException($"热身位移不可为负，实得 {WarmupCounts}");
        if (WarmupSettleMs < 0) throw new LoopException($"热身等待不可为负，实得 {WarmupSettleMs}");
        if (ProbeMagnitudes is null || ProbeMagnitudes.Count == 0)
            throw new LoopException("探头量级档表不可为空");
        for (var i = 0; i < ProbeMagnitudes.Count; i++)
        {
            if (ProbeMagnitudes[i] <= 0) throw new LoopException($"探头量级须为正，第 {i} 档实得 {ProbeMagnitudes[i]}");
            if (i > 0 && ProbeMagnitudes[i] <= ProbeMagnitudes[i - 1])
                throw new LoopException($"探头量级档须严格升序，第 {i - 1}/{i} 档实得 {ProbeMagnitudes[i - 1]}/{ProbeMagnitudes[i]}");
        }

        if (ProbesPerMagnitude <= 0) throw new LoopException($"每档探头次数须为正，实得 {ProbesPerMagnitude}");
        if (ProbeSettleMs < 0) throw new LoopException($"探头等待不可为负，实得 {ProbeSettleMs}");
        if (MinMeasuredDisplacementPx <= 0) throw new LoopException($"最小实测位移须为正，实得 {MinMeasuredDisplacementPx}");
        if (MinPpc <= 0) throw new LoopException($"ppc 下限须为正，实得 {MinPpc}");
        if (MaxPpc <= MinPpc) throw new LoopException($"ppc 上限须大于下限，实得 [{MinPpc}, {MaxPpc}]");
        if (OnlineEmaWeight is <= 0 or > 1) throw new LoopException($"在线校正权重须在 (0,1]，实得 {OnlineEmaWeight}");
        if (OnlineMinCommandCounts <= 0) throw new LoopException($"在线校正指令门槛须为正，实得 {OnlineMinCommandCounts}");
        if (OnlineMinMovedPx <= 0) throw new LoopException($"在线校正位移门槛须为正，实得 {OnlineMinMovedPx}");
        if (OnlineRelativeChangeThreshold < 0) throw new LoopException($"在线校正变化门槛不可为负，实得 {OnlineRelativeChangeThreshold}");
        if (Gain is <= 0 or > 1) throw new LoopException($"欠驱动增益须在 (0,1]，实得 {Gain}");
        if (ChunkThreshold <= 0) throw new LoopException($"分片阈值须为正，实得 {ChunkThreshold}");
        if (ChunkDelayMs < 0) throw new LoopException($"分片延迟不可为负，实得 {ChunkDelayMs}");

        return this;
    }
}
