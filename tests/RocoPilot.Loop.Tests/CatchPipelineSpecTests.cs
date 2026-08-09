namespace RocoPilot.Loop.Tests;

public class CatchPipelineSpecTests
{
    [Fact]
    public void DefaultsUseLiveModeAndGameWindow()
    {
        var spec = new CatchPipelineSpec();
        Assert.Equal(CatchLoopMode.Live, spec.Mode);
        Assert.Equal("洛克王国", spec.WindowTitleSubstring);
        Assert.False(spec.CalibrateBeforeThrow);
        Assert.False(spec.UseGpu);
    }

    [Fact]
    public void DefaultComponentOptionsPresent()
    {
        var spec = new CatchPipelineSpec();
        Assert.NotNull(spec.Detection);
        Assert.NotNull(spec.Centering);
        Assert.NotNull(spec.Loop);
    }

    [Fact]
    public void AllFactoriesAreWired()
    {
        var factories = new CatchPipelineFactories();
        Assert.NotNull(factories.Detector);
        Assert.NotNull(factories.Capture);
        Assert.NotNull(factories.Driver);
        Assert.NotNull(factories.IsGameForeground);
    }

    [Fact]
    public void CustomValuesRoundTrip()
    {
        var spec = new CatchPipelineSpec
        {
            Mode = CatchLoopMode.DryRun,
            WindowTitleSubstring = "My Game",
            UseGpu = true,
            CalibrateBeforeThrow = true,
            DetectionIntervalMs = 250,
        };
        Assert.Equal(CatchLoopMode.DryRun, spec.Mode);
        Assert.Equal("My Game", spec.WindowTitleSubstring);
        Assert.True(spec.UseGpu);
        Assert.True(spec.CalibrateBeforeThrow);
        Assert.Equal(250, spec.DetectionIntervalMs);
    }
}