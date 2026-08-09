namespace RocoPilot.Loop.Tests;

public class CatchLoopOptionsTests
{
    [Fact]
    public void DefaultsNormalizeWithoutError()
    {
        var opts = new CatchLoopOptions().Normalized();
        Assert.IsType<CatchLoopOptions>(opts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveSettleMs(int settleMs)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { SettleMs = settleMs }.Normalized());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void RejectsNonPositivePostSettleDelayMin(int delayMs)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { PostSettleDelayMinMs = delayMs }.Normalized());
    }

    [Fact]
    public void RejectsMaxDelayBelowMinDelay()
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { PostSettleDelayMinMs = 200, PostSettleDelayMaxMs = 100 }.Normalized());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-5.0)]
    public void RejectsNegativeAimJitterPx(double jitter)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { AimJitterPx = jitter }.Normalized());
    }

    [Fact]
    public void RejectsNonFiniteAimJitterPx()
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { AimJitterPx = double.NaN }.Normalized());
        Assert.Throws<LoopException>(() => new CatchLoopOptions { AimJitterPx = double.PositiveInfinity }.Normalized());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-3.0)]
    public void RejectsNegativeCommandNoise(double noise)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { CommandNoiseCounts = noise }.Normalized());
    }

    [Fact]
    public void RejectsNonFiniteCommandNoise()
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { CommandNoiseCounts = double.NegativeInfinity }.Normalized());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveChargeMs(int chargeMs)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { ChargeMs = chargeMs }.Normalized());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    [InlineData(200, 250)]
    public void RejectsChargeJitterNotBelowBase(int chargeMs, int jitterMs)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { ChargeMs = chargeMs, ChargeJitterMs = jitterMs }.Normalized());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveStallAlertMs(int stallMs)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { StallAlertMs = stallMs }.Normalized());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveMaxAttempts(int maxAttempts)
    {
        Assert.Throws<LoopException>(() => new CatchLoopOptions { MaxAttempts = maxAttempts }.Normalized());
    }

    [Fact]
    public void ValidConfiguredValuesNormalize()
    {
        var opts = new CatchLoopOptions
        {
            SettleMs = 500,
            PostSettleDelayMinMs = 200,
            PostSettleDelayMaxMs = 400,
            AimJitterPx = 1.5,
            CommandNoiseCounts = 0.2,
            ChargeMs = 300,
            ChargeJitterMs = 20,
            StallAlertMs = 1000,
            MaxAttempts = 5,
        }.Normalized();
        Assert.Equal(500, opts.SettleMs);
        Assert.Equal(400, opts.PostSettleDelayMaxMs);
        Assert.Equal(20, opts.ChargeJitterMs);
    }
}