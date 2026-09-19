namespace RocoPilot.Tools.AutoThrow.Tests;

public class AutoThrowMappingsTests
{
    [Fact]
    public void ToLoopOptionsCarriesFastThrow()
    {
        var settings = Sanitized(new AutoThrowSettings
        {
            FastThrowEnabled = true,
            ChargeMs = 300,
            ChargeJitterMs = 25,
            ThrowIntervalSeconds = 0.5,
        });

        var loop = settings.ToLoopOptions();

        Assert.True(loop.FastThrowEnabled);
        Assert.Equal(300, loop.ChargeMs);
        Assert.Equal(25, loop.ChargeJitterMs);
        Assert.Equal(500, loop.PostSettleDelayMinMs);
        Assert.Equal(500, loop.PostSettleDelayMaxMs);
    }

    [Fact]
    public void ToPipelineSpecCarriesFastThrowThroughLoop()
    {
        var settings = Sanitized(new AutoThrowSettings { FastThrowEnabled = true });

        var spec = settings.ToPipelineSpec();

        Assert.True(spec.Loop.FastThrowEnabled);
    }

    [Fact]
    public void ToLoopOptionsDefaultsToChargeThrow()
    {
        var settings = Sanitized(new AutoThrowSettings());

        var loop = settings.ToLoopOptions();

        Assert.False(loop.FastThrowEnabled);
    }

    private static AutoThrowSettings Sanitized(AutoThrowSettings settings)
    {
        settings.SanitizeInPlace();
        return settings;
    }
}
