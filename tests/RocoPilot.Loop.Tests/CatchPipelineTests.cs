namespace RocoPilot.Loop.Tests;

public class CatchPipelineTests
{
    [Fact]
    public void DefaultSpecBuildsFourArmingSteps()
    {
        var pipe = new CatchPipeline();
        Assert.Equal(4, pipe.ArmingSteps.Count);
        Assert.Equal(new[] { "detector", "input", "capture", "engine" },
            pipe.ArmingSteps.Select(s => s.Name));
    }

    [Fact]
    public void CalibrateBeforeThrowAddsCalibrationStep()
    {
        var pipe = new CatchPipeline(new CatchPipelineSpec { CalibrateBeforeThrow = true });
        Assert.Equal(5, pipe.ArmingSteps.Count);
        Assert.Contains(pipe.ArmingSteps, s => s.Name == "calibration");
    }

    [Fact]
    public void CalibrateBeforeThrowKeepsDefaultStepCount()
    {
        var pipe = new CatchPipeline();
        Assert.DoesNotContain(pipe.ArmingSteps, s => s.Name == "calibration");
    }

    [Fact]
    public void BusThrowsBeforeArming()
    {
        var pipe = new CatchPipeline();
        Assert.Throws<InvalidOperationException>(() => _ = pipe.Bus);
    }

    [Fact]
    public void DefaultObservationsAreEmpty()
    {
        var pipe = new CatchPipeline();
        Assert.Empty(pipe.ObserveDetections());
        Assert.Equal((0, 0), pipe.SensorFrameSize);
        Assert.Equal(-1, pipe.ActiveTrackId);
    }

    [Fact]
    public void InputGateFalseWithoutGameWindow()
    {
        var pipe = new CatchPipeline();
        Assert.False(pipe.InputGate());
    }

    [Fact]
    public void ControlCommandsAreNoOpsBeforeArming()
    {
        var pipe = new CatchPipeline();
        pipe.Pause();
        pipe.Resume();
        pipe.SetSensing(false);
        pipe.SetSensing(true);
        pipe.Dispose();
    }

    [Fact]
    public void ApplyLoopOptionsIsNoOpBeforeArming()
    {
        var pipe = new CatchPipeline();
        pipe.ApplyLoopOptions(new CatchLoopOptions { StallAlertMs = 1 });
        pipe.Dispose();
    }
}