namespace RocoPilot.Loop.Tests;

public class SceneShiftEstimatorTests
{
    private const int W = 200;
    private const int H = 200;

    private static byte[] BuildFrame(Func<int, int, int> grayAt)
    {
        var bgra = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var v = (byte)Math.Clamp(grayAt(x, y), 0, 255);
                var i = (y * W + x) * 4;
                bgra[i] = v;
                bgra[i + 1] = v;
                bgra[i + 2] = v;
                bgra[i + 3] = 0;
            }
        }

        return bgra;
    }

    [Fact]
    public void FrameTooSmallReturnsNull()
    {
        Assert.Null(SceneShiftEstimator.Estimate(new byte[100], new byte[100], 8, 8));
    }

    [Fact]
    public void MaxShiftTooSmallReturnsNull()
    {
        var a = BuildFrame((x, _) => x);
        Assert.Null(SceneShiftEstimator.Estimate(a, a, W, H, maxShiftPx: 1));
    }

    [Fact]
    public void IdenticalFramesReturnZeroShift()
    {
        var a = BuildFrame((x, _) => x);
        var (dx, dy) = SceneShiftEstimator.Estimate(a, a, W, H)!.Value;
        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void HorizontalShiftIsDetectedAtDownsampledScale()
    {
        var a = BuildFrame((x, _) => x);
        var b = BuildFrame((x, _) => Math.Clamp(x - 8, 0, 255));
        var r = SceneShiftEstimator.Estimate(a, b, W, H);
        Assert.NotNull(r);
        Assert.NotEqual(0, r.Value.Dx);
        Assert.Equal(0, r.Value.Dy);
        Assert.Equal(8, Math.Abs(r.Value.Dx));
    }
}