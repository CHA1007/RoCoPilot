using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace RocoPilot.Dispatch;

/// <summary>
/// 模板匹配场景检测基类（OpenCvSharp NCC）。
/// 子类只需指定场景、模板图路径和搜索区域比例。
/// </summary>
public class TemplateSceneDetector : ISceneDetector, IDisposable
{
    private readonly Mat _template;
    private readonly double _threshold;

    /// <param name="scene">该检测器负责的场景。</param>
    /// <param name="templatePath">模板 PNG 路径。</param>
    /// <param name="searchRegion">搜索区域比例 (x%, y%, w%, h%)，均 [0,1]。</param>
    /// <param name="threshold">NCC 得分阈值，超过则判定为检出。默认 0.8。</param>
    public TemplateSceneDetector(
        GameScene scene,
        string templatePath,
        (double X, double Y, double W, double H) searchRegion,
        double threshold = 0.8)
    {
        Scene = scene;
        SearchRegion = searchRegion;
        _threshold = threshold;

        ArgumentException.ThrowIfNullOrEmpty(templatePath);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"模板图不存在：{templatePath}", templatePath);

        // 读模板（RGBA → BGR）
        var raw = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (raw.Empty())
            throw new InvalidOperationException($"模板图无法解码：{templatePath}");
        _template = raw;
        Trace.TraceInformation($"[SceneDetector] {scene} 模板已加载：{templatePath} ({_template.Cols}x{_template.Rows})");
    }

    public GameScene Scene { get; }

    /// <summary>搜索区域比例坐标 (x, y, w, h)，均 [0,1]。</summary>
    public (double X, double Y, double W, double H) SearchRegion { get; }

    public float Detect(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (bgraPixels.IsEmpty || width <= 0 || height <= 0)
            return 0f;

        try
        {
            // BGRA byte[] → Mat（Marshal.Copy 确保数据可靠传入）
            var data = bgraPixels.ToArray();
            using var frame = new Mat(height, width, MatType.CV_8UC4);
            Marshal.Copy(data, 0, frame.Data, data.Length);

            using var bgr = new Mat();
            Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);

            // 按比例裁搜索区域
            var (rx, ry, rw, rh) = SearchRegion;
            var roiX = (int)(rx * width);
            var roiY = (int)(ry * height);
            var roiW = Math.Min((int)(rw * width), width - roiX);
            var roiH = Math.Min((int)(rh * height), height - roiY);

            // 模板比搜索区域大时跳过
            if (roiW < _template.Cols || roiH < _template.Rows)
                return 0f;

            using var region = new Mat(bgr, new Rect(roiX, roiY, roiW, roiH));
            using var result = new Mat();
            Cv2.MatchTemplate(region, _template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            var score = maxVal >= _threshold ? (float)maxVal : 0f;
            Trace.TraceInformation($"[SceneDetector] {Scene}: score={maxVal:F4} threshold={_threshold} roi=({roiX},{roiY},{roiW},{roiH}) frame=({width}x{height})");
            return score;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[SceneDetector] {Scene} 检测异常：{ex.GetBaseException().Message}");
            return 0f;
        }
    }

    public void Dispose() => _template.Dispose();
}
