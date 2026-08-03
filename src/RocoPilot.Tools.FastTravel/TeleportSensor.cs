using System.Diagnostics;
using System.IO;
using OpenCvSharp;

namespace RocoPilot.Tools.FastTravel;

public sealed class TeleportSensor : IDisposable
{

    private const double RegionX = 0.66, RegionY = 0.84, RegionW = 0.34, RegionH = 0.16;
    private const double Threshold = 0.70;

    private readonly Mat _template;

    private TeleportSensor(Mat template) => _template = template;

    public static TeleportSensor? TryCreate(string templatePath)
    {
        if (!File.Exists(templatePath))
            return null;

        var mat = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (mat.Empty())
        {
            mat.Dispose();
            return null;
        }

        return new TeleportSensor(mat);
    }

    public (int X, int Y)? Find(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (bgraPixels.IsEmpty || width <= 0 || height <= 0)
            return null;

        try
        {
            using var frame = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgraPixels.ToArray());
            using var bgr = new Mat();
            Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);

            var roiX = (int)(RegionX * width);
            var roiY = (int)(RegionY * height);
            var roiW = Math.Min((int)(RegionW * width), width - roiX);
            var roiH = Math.Min((int)(RegionH * height), height - roiY);
            if (roiW < _template.Cols || roiH < _template.Rows)
                return null;

            using var region = new Mat(bgr, new Rect(roiX, roiY, roiW, roiH));
            using var result = new Mat();
            Cv2.MatchTemplate(region, _template, result, TemplateMatchModes.CCoeffNormed);

            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
            if (maxVal < Threshold)
                return null;

            var center = (
                X: roiX + maxLoc.X + _template.Cols / 2,
                Y: roiY + maxLoc.Y + _template.Rows / 2);
            Trace.TraceInformation($"[TeleportSensor] hit score={maxVal:F4} center=({center.X},{center.Y})");
            return center;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _template.Dispose();
}
