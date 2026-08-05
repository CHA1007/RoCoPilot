using System.Diagnostics;
using RocoPilot.Dispatch;

namespace RocoPilot.Tools.FastTravel;

public sealed class TeleportSensor : IDisposable
{
    private const double RegionX = 0.66, RegionY = 0.84, RegionW = 0.34, RegionH = 0.16;
    private const double Threshold = 0.70;

    private readonly TemplateMatcher _matcher;

    private TeleportSensor(TemplateMatcher matcher) => _matcher = matcher;

    public static TeleportSensor? TryCreate(string templatePath)
    {
        var matcher = TemplateMatcher.TryLoad(templatePath, (RegionX, RegionY, RegionW, RegionH), Threshold);
        return matcher is null ? null : new TeleportSensor(matcher);
    }

    public (int X, int Y)? Find(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        try
        {
            if (_matcher.Find(bgraPixels, width, height) is not { } hit)
                return null;

            Trace.TraceInformation($"[TeleportSensor] hit score={hit.Score:F4} center=({hit.X},{hit.Y})");
            return (hit.X, hit.Y);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _matcher.Dispose();
}
