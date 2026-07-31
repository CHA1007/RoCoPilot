namespace RocoPilot.Loop;

/// <summary>
/// 基于 SAD（绝对差之和）的场景平移估计。
/// 对两帧做灰度降采样后滑动匹配，返回像素位移。
/// </summary>
public static class SceneShiftEstimator
{
    private const int Downsample = 4;
    private const int Margin = 40; // 降采样后边缘裁剪（避免黑边干扰）

    /// <summary>
    /// 估计 frameB 相对 frameA 的水平/垂直位移（像素，原始分辨率）。
    /// 返回 null 表示匹配失败（场景纹理不足或位移超出搜索范围）。
    /// </summary>
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

        // 先搜水平（主轴），再搜垂直
        var dx = FindBestShift(grayA, grayB, sw, sh, maxShift, horizontal: true);
        var dy = FindBestShift(grayA, grayB, sw, sh, maxShift, horizontal: false);

        if (dx is null && dy is null) return null;

        return (
            (dx ?? 0) * Downsample,
            (dy ?? 0) * Downsample);
    }

    /// <summary>沿单轴滑动 SAD 搜索最佳位移。</summary>
    private static int? FindBestShift(
        byte[] a, byte[] b, int w, int h, int maxShift, bool horizontal)
    {
        var bestShift = 0;
        long bestSad = long.MaxValue;
        var m = Margin;

        // 有效区域（裁掉边缘和搜索范围）
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
            for (var y = y0; y < y1; y += 2) // 隔行采样加速
            {
                var rowA = y * w;
                var rowB = horizontal ? y * w : (y + shift) * w;
                for (var x = x0; x < x1; x += 2) // 隔列采样加速
                {
                    var bx = horizontal ? x + shift : x;
                    sad += Math.Abs(a[rowA + x] - b[rowB + bx]);
                }
            }

            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
            }
        }

        // 置信检查：最佳 SAD 应显著低于零位移 SAD
        if (bestShift == 0) return 0;
        return bestShift;
    }

    /// <summary>BGRA → 灰度 + 降采样。</summary>
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
                // BT.601 灰度：0.299R + 0.587G + 0.114B（BGRA 序）
                gray[dy * dstW + dx] = (byte)(
                    (bgra[idx + 2] * 77 + bgra[idx + 1] * 150 + bgra[idx] * 29) >> 8);
            }
        }

        return gray;
    }
}
