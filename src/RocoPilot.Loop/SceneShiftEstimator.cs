namespace RocoPilot.Loop;

public static class SceneShiftEstimator
{
    private const int Downsample = 4;
    private const int Margin = 40;

    public static (double Dx, double Dy)? Estimate(
        ReadOnlySpan<byte> frameA, ReadOnlySpan<byte> frameB,
        int width, int height, int maxShiftPx = 300)
    {
        if (width < Downsample * 4 || height < Downsample * 4) return null;

        var sw = width / Downsample;
        var sh = height / Downsample;
        var grayA = ToGrayDownsampled(frameA, width, height, sw, sh);
        var grayB = ToGrayDownsampled(frameB, width, height, sw, sh);

        var maxShift = Math.Min(maxShiftPx / Downsample, Math.Min(sw, sh) / 3);
        if (maxShift < 2) return null;

        var dx = FindBestShift(grayA, grayB, sw, sh, maxShift, horizontal: true);
        var dy = FindBestShift(grayA, grayB, sw, sh, maxShift, horizontal: false);

        if (dx is null && dy is null) return null;

        return (
            (dx ?? 0) * Downsample,
            (dy ?? 0) * Downsample);
    }

    private static int? FindBestShift(
        byte[] a, byte[] b, int w, int h, int maxShift, bool horizontal)
    {
        var bestShift = 0;
        long bestSad = long.MaxValue;
        long zeroSad = 0;
        var m = Margin;

        int x0, x1, y0, y1;
        if (horizontal)
        {
            x0 = m + maxShift; x1 = w - m - maxShift;
            y0 = m; y1 = h - m;
        }
        else
        {
            x0 = m; x1 = w - m;
            y0 = m + maxShift; y1 = h - m - maxShift;
        }

        if (x1 - x0 < 8 || y1 - y0 < 8) return null;

        for (var shift = -maxShift; shift <= maxShift; shift++)
        {
            long sad = 0;
            for (var y = y0; y < y1; y += 2)
            {
                var rowA = y * w;
                var rowB = horizontal ? y * w : (y + shift) * w;
                for (var x = x0; x < x1; x += 2)
                {
                    var bx = horizontal ? x + shift : x;
                    sad += Math.Abs(a[rowA + x] - b[rowB + bx]);
                }
            }

            if (shift == 0) zeroSad = sad;
            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
            }
        }

        if (bestShift == 0) return 0;

        if (zeroSad == 0 || bestSad > zeroSad * 0.85) return null;
        return bestShift;
    }

    private static byte[] ToGrayDownsampled(ReadOnlySpan<byte> bgra, int srcW, int srcH, int dstW, int dstH)
    {
        var gray = new byte[dstW * dstH];
        for (var dy = 0; dy < dstH; dy++)
        {
            var sy = dy * Downsample;
            for (var dx = 0; dx < dstW; dx++)
            {
                var sx = dx * Downsample;
                var idx = (sy * srcW + sx) * 4;

                gray[dy * dstW + dx] = (byte)(
                    (bgra[idx + 2] * 77 + bgra[idx + 1] * 150 + bgra[idx] * 29) >> 8);
            }
        }

        return gray;
    }
}
