using System.Diagnostics;
using OpenCvSharp;

namespace RocoPilot.Dispatch;

public sealed class TemplateMatcher : IDisposable
{
    private const int MaxHits = 64;

    public TemplateMatcher(
        TemplateImage template,
        (double X, double Y, double W, double H) searchRegion,
        double threshold)
    {
        Template = template;
        SearchRegion = searchRegion;
        Threshold = threshold;
    }

    public TemplateImage Template { get; }

    public (double X, double Y, double W, double H) SearchRegion { get; }

    public double Threshold { get; }

    public static TemplateMatcher Load(
        string templatePath,
        (double X, double Y, double W, double H) searchRegion,
        double threshold)
    {
        return new TemplateMatcher(TemplateImage.Load(templatePath), searchRegion, threshold);
    }

    public static TemplateMatcher? TryLoad(
        string templatePath,
        (double X, double Y, double W, double H) searchRegion,
        double threshold)
    {
        try
        {
            return Load(templatePath, searchRegion, threshold);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[TemplateMatcher] 模板加载失败 {templatePath}：{ex.GetBaseException().Message}");
            return null;
        }
    }

    public double BestScore(ReadOnlySpan<byte> bgraPixels, int width, int height)
        => Best(bgraPixels, width, height)?.Score ?? 0;

    public TemplateHit? Find(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        var best = Best(bgraPixels, width, height);
        return best is { } hit && hit.Score >= Threshold ? hit : null;
    }

    public IReadOnlyList<TemplateHit> FindAll(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (!TryBuildRegion(bgraPixels, width, height, out var bgr, out var roiX, out var roiY, out var roiW, out var roiH))
            return [];

        try
        {
            using var region = new Mat(bgr, new Rect(roiX, roiY, roiW, roiH));
            using var result = new Mat();
            Template.Match(region, result);
            Sanitize(result);

            var hits = new List<TemplateHit>();
            for (var i = 0; i < MaxHits; i++)
            {
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
                if (maxVal < Threshold) break;

                hits.Add(new TemplateHit(
                    roiX + maxLoc.X + Template.Width / 2,
                    roiY + maxLoc.Y + Template.Height / 2,
                    maxVal));
                Suppress(result, maxLoc);
            }

            return hits;
        }
        finally
        {
            bgr.Dispose();
        }
    }

    private TemplateHit? Best(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (!TryBuildRegion(bgraPixels, width, height, out var bgr, out var roiX, out var roiY, out var roiW, out var roiH))
            return null;

        try
        {
            using var region = new Mat(bgr, new Rect(roiX, roiY, roiW, roiH));
            using var result = new Mat();
            Template.Match(region, result);
            Sanitize(result);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            return new TemplateHit(
                roiX + maxLoc.X + Template.Width / 2,
                roiY + maxLoc.Y + Template.Height / 2,
                maxVal);
        }
        finally
        {
            bgr.Dispose();
        }
    }

    private bool TryBuildRegion(
        ReadOnlySpan<byte> bgraPixels, int width, int height,
        out Mat bgr, out int roiX, out int roiY, out int roiW, out int roiH)
    {
        bgr = null!;
        roiX = roiY = roiW = roiH = 0;

        if (bgraPixels.IsEmpty || width <= 0 || height <= 0)
            return false;

        var (rx, ry, rw, rh) = SearchRegion;
        roiX = Math.Max(0, (int)(rx * width));
        roiY = Math.Max(0, (int)(ry * height));
        roiW = Math.Min((int)(rw * width), width - roiX);
        roiH = Math.Min((int)(rh * height), height - roiY);

        if (roiW < Template.Width || roiH < Template.Height)
            return false;

        using var frame = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgraPixels.ToArray());
        bgr = new Mat();
        Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);
        return true;
    }

    private static void Sanitize(Mat result)
    {
        Cv2.PatchNaNs(result);
        using var garbage = new Mat();
        Cv2.Compare(result, new Scalar(1e6), garbage, CmpType.GT);
        result.SetTo(new Scalar(-2), garbage);
        Cv2.Min(result, new Scalar(1), result);
        Cv2.Max(result, new Scalar(-1), result);
    }

    private void Suppress(Mat result, Point maxLoc)
    {
        var x = Math.Max(0, maxLoc.X - Template.Width / 2);
        var y = Math.Max(0, maxLoc.Y - Template.Height / 2);
        var rectWidth = Math.Min(Template.Width, result.Cols - x);
        var rectHeight = Math.Min(Template.Height, result.Rows - y);
        if (rectWidth <= 0 || rectHeight <= 0) return;

        using var roi = new Mat(result, new Rect(x, y, rectWidth, rectHeight));
        roi.SetTo(new Scalar(-2));
    }

    public void Dispose() => Template.Dispose();
}
