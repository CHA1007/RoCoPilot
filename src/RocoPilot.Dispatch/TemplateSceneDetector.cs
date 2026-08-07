using System.Diagnostics;

namespace RocoPilot.Dispatch;

public class TemplateSceneDetector : ISceneDetector, IDisposable
{
    private readonly TemplateMatcher _matcher;

    public TemplateSceneDetector(
        GameScene scene,
        string templatePath,
        (double X, double Y, double W, double H) searchRegion,
        double threshold = 0.8)
    {
        Scene = scene;
        _matcher = TemplateMatcher.Load(templatePath, searchRegion, threshold);
        Trace.TraceInformation($"[SceneDetector] {scene} 模板已加载：{templatePath} ({_matcher.Template.Width}x{_matcher.Template.Height})");
    }

    public GameScene Scene { get; }

    public float LastRawScore { get; private set; }

    public (double X, double Y, double W, double H) SearchRegion => _matcher.SearchRegion;

    public float Detect(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        try
        {
            var maxVal = _matcher.BestScore(bgraPixels, width, height);
            LastRawScore = (float)maxVal;

            var (rx, ry, rw, rh) = _matcher.SearchRegion;
            var roiX = (int)(rx * width);
            var roiY = (int)(ry * height);
            var roiW = Math.Min((int)(rw * width), width - roiX);
            var roiH = Math.Min((int)(rh * height), height - roiY);
            Trace.TraceInformation($"[SceneDetector] {Scene}: score={maxVal:F4} threshold={_matcher.Threshold} roi=({roiX},{roiY},{roiW},{roiH}) frame=({width}x{height})");

            return maxVal >= _matcher.Threshold ? (float)maxVal : 0f;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[SceneDetector] {Scene} 检测异常：{ex.GetBaseException().Message}");
            return 0f;
        }
    }

    public void Dispose() => _matcher.Dispose();
}
