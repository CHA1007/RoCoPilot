namespace RocoPilot.Detection;

internal readonly record struct LetterboxGeometry(int NewWidth, int NewHeight, int PadLeft, int PadTop, double Ratio);

internal static class YoloPreprocessing
{
    private const float PadValue = 114f / 255f;
    private const float Inv255 = 1f / 255f;

    internal static LetterboxGeometry ComputeLetterbox(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var ratio = Math.Min((double)targetHeight / sourceHeight, (double)targetWidth / sourceWidth);
        var newWidth = (int)Math.Round(sourceWidth * ratio);
        var newHeight = (int)Math.Round(sourceHeight * ratio);
        var padLeft = (int)Math.Round((targetWidth - newWidth) / 2.0 - 0.1);
        var padTop = (int)Math.Round((targetHeight - newHeight) / 2.0 - 0.1);
        return new LetterboxGeometry(newWidth, newHeight, padLeft, padTop, ratio);
    }

    internal static LetterboxGeometry LetterboxToTensor(
        ReadOnlySpan<byte> bgraPixels, int width, int height,
        int targetWidth, int targetHeight, Span<float> destination)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"源帧尺寸须为正，实得 {width}×{height}");
        if (targetWidth <= 0 || targetHeight <= 0)
            throw new ArgumentException($"目标形状须为正，实得 {targetWidth}×{targetHeight}");
        if (bgraPixels.Length < width * height * 4)
            throw new ArgumentException($"像素缓冲不足 {width}×{height}×4：须 ≥ {width * height * 4}，实得 {bgraPixels.Length}");
        if (destination.Length != 3 * targetWidth * targetHeight)
            throw new ArgumentException($"张量缓冲须恰长 {3 * targetWidth * targetHeight}（3×{targetWidth}×{targetHeight}），实得 {destination.Length}");

        var geometry = ComputeLetterbox(width, height, targetWidth, targetHeight);
        if (geometry.NewWidth <= 0 || geometry.NewHeight <= 0)
            throw new ArgumentException($"源帧宽高比极端（{width}×{height} → {geometry.NewWidth}×{geometry.NewHeight}），缩入后无内容像素");

        destination.Fill(PadValue);

        var plane = targetWidth * targetHeight;
        var scaleX = (double)width / geometry.NewWidth;
        var scaleY = (double)height / geometry.NewHeight;

        for (var dy = 0; dy < geometry.NewHeight; dy++)
        {
            var srcYf = (dy + 0.5) * scaleY - 0.5;
            var y0 = (int)Math.Floor(srcYf);
            var fy = (float)(srcYf - y0);
            var sy0 = Math.Clamp(y0, 0, height - 1) * width;
            var sy1 = Math.Clamp(y0 + 1, 0, height - 1) * width;
            var destRow = (geometry.PadTop + dy) * targetWidth + geometry.PadLeft;

            for (var dx = 0; dx < geometry.NewWidth; dx++)
            {
                var srcXf = (dx + 0.5) * scaleX - 0.5;
                var x0 = (int)Math.Floor(srcXf);
                var fx = (float)(srcXf - x0);
                var sx0 = Math.Clamp(x0, 0, width - 1);
                var sx1 = Math.Clamp(x0 + 1, 0, width - 1);

                var p00 = (sy0 + sx0) * 4;
                var p01 = (sy0 + sx1) * 4;
                var p10 = (sy1 + sx0) * 4;
                var p11 = (sy1 + sx1) * 4;
                var w00 = (1f - fx) * (1f - fy);
                var w01 = fx * (1f - fy);
                var w10 = (1f - fx) * fy;
                var w11 = fx * fy;

                var idx = destRow + dx;
                destination[idx] = Sample(bgraPixels, p00, p01, p10, p11, channelOffset: 2, w00, w01, w10, w11);
                destination[plane + idx] = Sample(bgraPixels, p00, p01, p10, p11, channelOffset: 1, w00, w01, w10, w11);
                destination[2 * plane + idx] = Sample(bgraPixels, p00, p01, p10, p11, channelOffset: 0, w00, w01, w10, w11);
            }
        }

        return geometry;
    }

    private static float Sample(
        ReadOnlySpan<byte> pixels, int p00, int p01, int p10, int p11, int channelOffset,
        float w00, float w01, float w10, float w11) =>
        (pixels[p00 + channelOffset] * w00
         + pixels[p01 + channelOffset] * w01
         + pixels[p10 + channelOffset] * w10
         + pixels[p11 + channelOffset] * w11) * Inv255;
}
