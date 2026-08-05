using OpenCvSharp;

namespace RocoPilot.Dispatch;

public sealed class TemplateImage : IDisposable
{
    private TemplateImage(Mat bgr, Mat? mask)
    {
        Bgr = bgr;
        Mask = mask;
    }

    public Mat Bgr { get; }

    public Mat? Mask { get; }

    public int Width => Bgr.Cols;

    public int Height => Bgr.Rows;

    public static TemplateImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"模板图不存在：{path}", path);

        using var raw = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (raw.Empty())
            throw new InvalidOperationException($"模板图无法解码：{path}");

        if (raw.Channels() == 3)
            return new TemplateImage(raw.Clone(), null);

        if (raw.Channels() != 4)
            throw new InvalidOperationException($"模板图通道数不支持：{path}（{raw.Channels()} 通道）");

        Cv2.Split(raw, out var channels);
        using var blue = channels[0];
        using var green = channels[1];
        using var red = channels[2];
        using var alpha = channels[3];

        var bgr = new Mat();
        Cv2.Merge(new[] { blue, green, red }, bgr);

        Cv2.MinMaxLoc(alpha, out var minAlpha, out _, out _, out _);
        if (minAlpha >= 255)
            return new TemplateImage(bgr, null);

        var mask = new Mat();
        Cv2.Merge(new[] { alpha, alpha, alpha }, mask);
        return new TemplateImage(bgr, mask);
    }

    public void Match(Mat image, Mat result)
    {
        if (Mask is null)
            Cv2.MatchTemplate(image, Bgr, result, TemplateMatchModes.CCoeffNormed);
        else
            Cv2.MatchTemplate(image, Bgr, result, TemplateMatchModes.CCoeffNormed, Mask);
    }

    public void Dispose()
    {
        Mask?.Dispose();
        Bgr.Dispose();
    }
}
