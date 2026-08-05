using System.Diagnostics;
using System.IO;
using OpenCvSharp;

namespace RocoPilot.Routing;

public sealed class PoiIconMatcher : IDisposable
{
    private const double Threshold = 0.90;

    private readonly Mat _template;
    private readonly Mat? _mask;

    private PoiIconMatcher(Mat template, Mat? mask)
    {
        _template = template;
        _mask = mask;
    }

    public static PoiIconMatcher? TryCreate(string templatePath)
    {
        if (!File.Exists(templatePath))
            return null;

        try
        {
            var raw = Cv2.ImRead(templatePath, ImreadModes.Unchanged);
            if (raw.Empty())
            {
                raw.Dispose();
                return null;
            }

            if (raw.Channels() == 4)
            {
                using var bgr = new Mat();
                Cv2.CvtColor(raw, bgr, ColorConversionCodes.BGRA2BGR);

                using var alpha = new Mat();
                Cv2.ExtractChannel(raw, alpha, 3);

                using var mask = new Mat();
                Cv2.CvtColor(alpha, mask, ColorConversionCodes.GRAY2BGR);

                return new PoiIconMatcher(bgr.Clone(), mask.Clone());
            }

            if (raw.Channels() == 3)
                return new PoiIconMatcher(raw.Clone(), null);

            raw.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[PoiIconMatcher] 模板加载失败 {templatePath}：{ex.GetBaseException().Message}");
            return null;
        }
    }

    public (int X, int Y, double Score)? Find(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (bgraPixels.IsEmpty || width <= 0 || height <= 0)
            return null;

        if (width < _template.Cols || height < _template.Rows)
            return null;

        try
        {
            using var frame = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgraPixels.ToArray());
            using var bgr = new Mat();
            Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);

            using var result = new Mat();
            if (_mask is null)
            {
                Cv2.MatchTemplate(bgr, _template, result, TemplateMatchModes.CCoeffNormed);
            }
            else
            {
                Cv2.MatchTemplate(bgr, _template, result, TemplateMatchModes.CCorrNormed, _mask);
            }

            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
            if (maxVal < Threshold)
                return null;

            var center = (
                X: maxLoc.X + _template.Cols / 2,
                Y: maxLoc.Y + _template.Rows / 2,
                Score: maxVal);
            Trace.TraceInformation($"[PoiIconMatcher] hit score={maxVal:F4} center=({center.X},{center.Y})");
            return center;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[PoiIconMatcher] 匹配异常：{ex.GetBaseException().Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _template.Dispose();
        _mask?.Dispose();
    }
}
