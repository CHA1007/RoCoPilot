using System.Diagnostics;
using RocoPilot.Dispatch;

namespace RocoPilot.Routing;

public sealed class PoiIconMatcher : IDisposable
{
    public const double DefaultThreshold = 0.90;

    private readonly TemplateMatcher _matcher;

    private PoiIconMatcher(TemplateMatcher matcher) => _matcher = matcher;

    public static PoiIconMatcher? TryCreate(string templatePath, double threshold = DefaultThreshold)
    {
        var matcher = TemplateMatcher.TryLoad(templatePath, (0, 0, 1, 1), threshold);
        return matcher is null ? null : new PoiIconMatcher(matcher);
    }

    public (int X, int Y, double Score)? Find(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        var hits = FindAll(bgraPixels, width, height);
        return hits.Count > 0 ? hits[0] : null;
    }

    public IReadOnlyList<(int X, int Y, double Score)> FindAll(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        var hits = _matcher.FindAll(bgraPixels, width, height);
        foreach (var hit in hits)
            Trace.TraceInformation($"[PoiIconMatcher] hit score={hit.Score:F4} center=({hit.X},{hit.Y})");
        return [.. hits.Select(h => (h.X, h.Y, h.Score))];
    }

    public void Dispose() => _matcher.Dispose();
}
