using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Loop.Tests;

internal sealed class FakeDriver : IInputDriver
{
    public List<(int Dx, int Dy)> Moves { get; } = [];

    public string BackendName => "fake";

    public void Arm() { }

    public void MoveRelative(int dx, int dy) => Moves.Add((dx, dy));

    public void KeyDown(InputKey key) { }

    public void KeyUp(InputKey key) { }

    public void SendRawStroke(ReceivedStroke stroke) { }

    public void StartStrokeRelay(Action<ReceivedStroke> onStroke) { }

    public void StopStrokeRelay() { }

    public void Dispose() { }
}

internal sealed class FakeSensor : ICenteringSensor
{
    private readonly Queue<(float X, float Y)> _positions;
    private readonly (int W, int H) _frameSize;

    public FakeSensor((int W, int H) frameSize, params (float X, float Y)[] positions)
    {
        _frameSize = frameSize;
        _positions = new Queue<(float X, float Y)>(positions);
    }

    public (int Width, int Height) LatestFrameSize => (_frameSize.W, _frameSize.H);

    public IReadOnlyList<StableTarget> ObserveStable()
    {
        if (_positions.Count == 0) return [];
        var (x, y) = _positions.Dequeue();
        var box = new DetectedBox(0, "test", 0.9f, x - 10, y - 10, x + 10, y + 10);
        return [new StableTarget(1, box, (x, y), 5)];
    }

    public void SuspendSensing() { }

    public void ResumeSensing() { }

    public void ResetStability() { }
}

public class CenteringGainTests
{
    private static (CenteringController Ctrl, FakeDriver Driver) MakeController(
        CenteringOptions options, params (float X, float Y)[] positions)
    {
        var driver = new FakeDriver();
        var sensor = new FakeSensor((1920, 1080), positions);
        var cache = new CalibrationCache();
        cache.Store(100, 1.0);
        var ctrl = new CenteringController(options, sensor, driver, cache,
            sleep: (_, _) => { });
        return (ctrl, driver);
    }

    [Fact]
    public void Gain_scales_command_down()
    {

        var options = new CenteringOptions
        {
            Gain = 0.5,
            TolerancePx = 5,
            MaxSteps = 1,
            ChunkThreshold = 9999,
        };

        var (ctrl, driver) = MakeController(options, (1060, 540), (1010, 540));

        ctrl.RunOnce();

        Assert.Single(driver.Moves);

        Assert.Equal(50, driver.Moves[0].Dx);
        Assert.Equal(0, driver.Moves[0].Dy);
    }

    [Fact]
    public void Gain_one_is_full_correction()
    {
        var options = new CenteringOptions
        {
            Gain = 1.0,
            TolerancePx = 5,
            MaxSteps = 1,
            ChunkThreshold = 9999,
        };
        var (ctrl, driver) = MakeController(options, (1060, 540), (960, 540));

        ctrl.RunOnce();

        Assert.Single(driver.Moves);
        Assert.Equal(100, driver.Moves[0].Dx);
    }
}

public class CenteringResidualTests
{
    [Fact]
    public void Residual_accumulates_across_steps()
    {

        var driver = new FakeDriver();
        var sensor = new FakeSensor((1920, 1080),
            (970, 540),
            (970, 540),
            (970, 540));
        var cache = new CalibrationCache();
        cache.Store(10, 3.0);
        var options = new CenteringOptions
        {
            Gain = 1.0,
            TolerancePx = 2,
            MaxSteps = 2,
            ChunkThreshold = 9999,
        };
        var ctrl = new CenteringController(options, sensor, driver, cache,
            sleep: (_, _) => { });

        ctrl.RunOnce();

        Assert.Equal(2, driver.Moves.Count);

        Assert.Equal(3, driver.Moves[0].Dx);

        Assert.Equal(4, driver.Moves[1].Dx);
    }
}

public class CenteringChunkTests
{
    [Fact]
    public void Large_move_is_split_into_chunks()
    {
        var driver = new FakeDriver();

        var sensor = new FakeSensor((1920, 1080),
            (1160, 540),
            (960, 540));
        var cache = new CalibrationCache();
        cache.Store(200, 1.0);
        var options = new CenteringOptions
        {
            Gain = 1.0,
            TolerancePx = 5,
            MaxSteps = 1,
            ChunkThreshold = 80,
            ChunkDelayMs = 0,
        };
        var ctrl = new CenteringController(options, sensor, driver, cache,
            sleep: (_, _) => { });

        ctrl.RunOnce();

        Assert.Equal(3, driver.Moves.Count);

        var totalDx = driver.Moves.Sum(m => m.Dx);
        Assert.Equal(200, totalDx);
    }

    [Fact]
    public void Small_move_is_single_stroke()
    {
        var driver = new FakeDriver();
        var sensor = new FakeSensor((1920, 1080),
            (1010, 540),
            (960, 540));
        var cache = new CalibrationCache();
        cache.Store(50, 5.0);
        var options = new CenteringOptions
        {
            Gain = 1.0,
            TolerancePx = 5,
            MaxSteps = 1,
            ChunkThreshold = 80,
        };
        var ctrl = new CenteringController(options, sensor, driver, cache,
            sleep: (_, _) => { });

        ctrl.RunOnce();

        Assert.Single(driver.Moves);
        Assert.Equal(10, driver.Moves[0].Dx);
    }
}

public class MouseAccelerationProbeTests
{
    [Fact]
    public void IsEnabled_does_not_throw()
    {

        var _ = MouseAccelerationProbe.IsEnabled();
    }
}

public class CenteringOptionsValidationTests
{
    [Fact]
    public void Gain_out_of_range_throws()
    {
        Assert.Throws<LoopException>(() => new CenteringOptions { Gain = 0 }.Normalized());
        Assert.Throws<LoopException>(() => new CenteringOptions { Gain = 1.1 }.Normalized());
    }

    [Fact]
    public void ChunkThreshold_non_positive_throws()
    {
        Assert.Throws<LoopException>(() => new CenteringOptions { ChunkThreshold = 0 }.Normalized());
    }

    [Fact]
    public void ChunkDelay_negative_throws()
    {
        Assert.Throws<LoopException>(() => new CenteringOptions { ChunkDelayMs = -1 }.Normalized());
    }

    [Fact]
    public void Defaults_are_valid()
    {
        var options = new CenteringOptions().Normalized();
        Assert.Equal(0.6, options.Gain);
        Assert.Equal(80, options.ChunkThreshold);
        Assert.Equal(10, options.ChunkDelayMs);
    }
}
