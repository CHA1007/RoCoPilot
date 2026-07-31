namespace RocoPilot.Loop;

public sealed record CatchLoopOptions
{
    public int SettleMs { get; init; } = 300;

    public int PostSettleDelayMinMs { get; init; } = 100;

    public int PostSettleDelayMaxMs { get; init; } = 100;

    public double AimJitterPx { get; init; }

    public double CommandNoiseCounts { get; init; }

    public int ChargeMs { get; init; } = 200;

    public int ChargeJitterMs { get; init; }

    public int StallAlertMs { get; init; } = 600_000;

    public int MaxAttempts { get; init; } = int.MaxValue;

    /// <summary>垂直瞄准偏移：框高的比例，负值=往上。</summary>
    public double AimOffsetY { get; init; } = -0.15;

    /// <summary>水平灵敏度（像素/count），0 则用 FallbackDivisor。</summary>
    public double PpcX { get; init; }

    /// <summary>垂直灵敏度（像素/count），0 则用 FallbackDivisor。</summary>
    public double PpcY { get; init; }

    internal CatchLoopOptions Normalized()
    {
        RejectNonFinite(AimJitterPx, "落点抖动幅");
        RejectNonFinite(CommandNoiseCounts, "每步修正噪声幅");

        if (SettleMs <= 0) throw new LoopException($"结算窗须为正，实得 {SettleMs}");
        if (PostSettleDelayMinMs <= 0) throw new LoopException($"投掷间隔下限须为正，实得 {PostSettleDelayMinMs}");
        if (PostSettleDelayMaxMs < PostSettleDelayMinMs)
            throw new LoopException($"投掷间隔上限须不小于下限，实得 [{PostSettleDelayMinMs}, {PostSettleDelayMaxMs}]");
        if (AimJitterPx < 0) throw new LoopException($"落点抖动幅不可为负，实得 {AimJitterPx}");
        if (CommandNoiseCounts < 0) throw new LoopException($"每步修正噪声幅不可为负，实得 {CommandNoiseCounts}");
        if (ChargeMs <= 0) throw new LoopException($"蓄力基值须为正，实得 {ChargeMs}");
        if (ChargeJitterMs < 0) throw new LoopException($"蓄力抖动幅不可为负，实得 {ChargeJitterMs}");
        if (ChargeJitterMs >= ChargeMs) throw new LoopException($"蓄力抖动幅须小于基值，实得基值 {ChargeMs} 抖 ±{ChargeJitterMs}");
        if (StallAlertMs <= 0) throw new LoopException($"stall 阈须为正，实得 {StallAlertMs}");
        if (MaxAttempts <= 0) throw new LoopException($"尝试上限须为正，实得 {MaxAttempts}");

        return this;
    }

    private static void RejectNonFinite(double value, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new LoopException($"{what}须为有限数，实得 {value}");
    }
}
