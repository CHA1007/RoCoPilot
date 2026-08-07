namespace RocoPilot.Routing;

public sealed record AnchorAlignment(
    double ScaleX,
    double ScaleY,
    double OffsetX,
    double OffsetY,
    IReadOnlyList<string> InlierNames)
{
    public (double X, double Y) Project(double lat, double lng)
        => (OffsetX + ScaleX * lng, OffsetY + ScaleY * lat);
}

public static class AnchorMapAligner
{
    private const double SearchTolerance = 0.1;
    private const int MaxPairHypotheses = 5000;
    private const int MaxRefineCandidates = 8;

    public static AnchorAlignment? Align(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        double inlierTolerance,
        int minInliers)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hits);
        if (catalog.Count < 2 || hits.Count < 2 || minInliers < 2) return null;
        if (hits.Count < minInliers) return null;

        var (lngMin, lngMax, latMin, latMax) = BoundingBox(catalog);
        var (xMin, xMax, yMin, yMax) = BoundingBox(hits);
        var lngSpan = lngMax - lngMin;
        var latSpan = latMax - latMin;
        var xSpan = xMax - xMin;
        var ySpan = yMax - yMin;
        if (lngSpan <= 0 || latSpan <= 0 || xSpan <= 0 || ySpan <= 0) return null;

        AnchorAlignment? best = null;
        var bestInliers = minInliers - 1;
        var bestResidual = double.MaxValue;
        var searchTolerance = Math.Max(inlierTolerance, SearchTolerance);

        var candidates = BoundingBoxSeeds(catalog, hits, lngSpan, latSpan, xSpan, ySpan)
            .Concat(PairHypotheses(catalog, hits))
            .ToList();

        var scored = new List<(AnchorAlignment Alignment, int Inliers, double Residual)>();
        foreach (var candidate in candidates)
        {
            var inliers = CountInliers(catalog, hits, candidate, searchTolerance, out var residual);
            scored.Add((candidate, inliers, residual));
        }

        foreach (var (candidate, _, _) in scored
                     .OrderByDescending(entry => entry.Inliers)
                     .ThenBy(entry => entry.Residual)
                     .Take(MaxRefineCandidates))
        {
            var refined = Refine(catalog, hits, candidate, searchTolerance);
            var inliers = CountInliers(catalog, hits, refined, inlierTolerance, out var residual);
            if (inliers > bestInliers || (inliers == bestInliers && residual < bestResidual))
            {
                best = refined;
                bestInliers = inliers;
                bestResidual = residual;
            }
        }

        if (best is null || bestInliers < minInliers) return null;

        var inlierNames = InlierNames(catalog, hits, best, inlierTolerance);
        return best with { InlierNames = inlierNames };
    }

    private static IEnumerable<AnchorAlignment> BoundingBoxSeeds(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        double lngSpan,
        double latSpan,
        double xSpan,
        double ySpan)
    {
        foreach (var signX in new[] { 1.0, -1.0 })
        {
            foreach (var signY in new[] { 1.0, -1.0 })
            {
                var scaleX = signX * xSpan / lngSpan;
                var scaleY = signY * ySpan / latSpan;
                if (SearchTranslation(catalog, hits, scaleX, scaleY, SearchTolerance) is { } candidate)
                    yield return candidate;
            }
        }
    }

    private static IEnumerable<AnchorAlignment> PairHypotheses(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits)
    {
        var catalogPairs = new List<(AnchorCatalogEntry A, AnchorCatalogEntry B)>();
        for (var i = 0; i < catalog.Count; i++)
        {
            for (var j = i + 1; j < catalog.Count; j++)
            {
                if (catalog[j].Lng != catalog[i].Lng && catalog[j].Lat != catalog[i].Lat)
                    catalogPairs.Add((catalog[i], catalog[j]));
            }
        }

        var hitPairs = new List<((double X, double Y) A, (double X, double Y) B)>();
        for (var i = 0; i < hits.Count; i++)
        {
            for (var j = i + 1; j < hits.Count; j++)
                hitPairs.Add((hits[i], hits[j]));
        }

        var total = catalogPairs.Count * hitPairs.Count * 2;
        if (total == 0) yield break;

        IEnumerable<(AnchorCatalogEntry A, AnchorCatalogEntry B, (double X, double Y) P, (double X, double Y) Q, bool Swap)> Sample()
        {
            if (total <= MaxPairHypotheses)
            {
                foreach (var (a, b) in catalogPairs)
                {
                    foreach (var (p, q) in hitPairs)
                    {
                        yield return (a, b, p, q, Swap: false);
                        yield return (a, b, q, p, Swap: true);
                    }
                }

                yield break;
            }

            var random = new Random(11);
            for (var iteration = 0; iteration < MaxPairHypotheses; iteration++)
            {
                var (a, b) = catalogPairs[random.Next(catalogPairs.Count)];
                var (p, q) = hitPairs[random.Next(hitPairs.Count)];
                yield return random.Next(2) == 0 ? (a, b, p, q, Swap: false) : (a, b, q, p, Swap: true);
            }
        }

        foreach (var (a, b, p, q, _) in Sample())
        {
            var scaleX = (q.X - p.X) / (b.Lng - a.Lng);
            var scaleY = (q.Y - p.Y) / (b.Lat - a.Lat);
            if (scaleX == 0 || scaleY == 0) continue;

            yield return new AnchorAlignment(
                scaleX,
                scaleY,
                p.X - scaleX * a.Lng,
                p.Y - scaleY * a.Lat,
                []);
        }
    }

    private static AnchorAlignment? SearchTranslation(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        double scaleX,
        double scaleY,
        double tolerance)
    {
        AnchorAlignment? best = null;
        var bestInliers = 0;

        foreach (var anchor in catalog)
        {
            foreach (var (x, y) in hits)
            {
                var candidate = new AnchorAlignment(
                    scaleX,
                    scaleY,
                    x - scaleX * anchor.Lng,
                    y - scaleY * anchor.Lat,
                    []);

                var inliers = CountInliers(catalog, hits, candidate, tolerance, out _);
                if (inliers > bestInliers)
                {
                    best = candidate;
                    bestInliers = inliers;
                }
            }
        }

        return best;
    }

    private static AnchorAlignment Refine(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        AnchorAlignment current,
        double tolerance)
    {
        var alignment = current;
        for (var pass = 0; pass < 2; pass++)
        {
            var pairs = Correspondences(catalog, hits, alignment, tolerance);
            if (pairs.Count < 3) return alignment;

            var (nextScaleX, nextOffsetX) = FitAxis(
                pairs.Select(pair => pair.Lng).ToList(),
                pairs.Select(pair => pair.HitX).ToList());
            var (nextScaleY, nextOffsetY) = FitAxis(
                pairs.Select(pair => pair.Lat).ToList(),
                pairs.Select(pair => pair.HitY).ToList());
            if (nextScaleX == 0 || nextScaleY == 0) return alignment;

            alignment = alignment with
            {
                ScaleX = nextScaleX,
                ScaleY = nextScaleY,
                OffsetX = nextOffsetX,
                OffsetY = nextOffsetY,
            };
        }

        return alignment;
    }

    private static List<(double Lng, double Lat, double HitX, double HitY)> Correspondences(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        AnchorAlignment alignment,
        double tolerance)
    {
        var pairs = new List<(double, double, double, double)>();
        foreach (var anchor in catalog)
        {
            var (px, py) = alignment.Project(anchor.Lat, anchor.Lng);
            var nearest = NearestHit(hits, px, py);
            if (nearest is not { } hit) continue;
            if (Distance(px, py, hit.X, hit.Y) > tolerance) continue;
            pairs.Add((anchor.Lng, anchor.Lat, hit.X, hit.Y));
        }

        return pairs;
    }

    private static (double Scale, double Offset) FitAxis(IReadOnlyList<double> sources, IReadOnlyList<double> targets)
    {
        var n = sources.Count;
        var meanSource = sources.Average();
        var meanTarget = targets.Average();

        var variance = 0.0;
        var covariance = 0.0;
        for (var i = 0; i < n; i++)
        {
            variance += (sources[i] - meanSource) * (sources[i] - meanSource);
            covariance += (sources[i] - meanSource) * (targets[i] - meanTarget);
        }

        if (variance <= 0) return (0, meanTarget);
        var scale = covariance / variance;
        return (scale, meanTarget - scale * meanSource);
    }

    private static int CountInliers(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        AnchorAlignment alignment,
        double tolerance,
        out double residual)
    {
        var inliers = 0;
        var sum = 0.0;
        foreach (var (x, y) in hits)
        {
            var distance = NearestProjectedDistance(catalog, alignment, x, y);
            if (distance > tolerance) continue;

            inliers++;
            sum += distance;
        }

        residual = inliers == 0 ? double.MaxValue : sum / inliers;
        return inliers;
    }

    private static IReadOnlyList<string> InlierNames(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        IReadOnlyList<(double X, double Y)> hits,
        AnchorAlignment alignment,
        double tolerance)
    {
        var names = new List<string>();
        foreach (var (x, y) in hits)
        {
            var bestDistance = double.MaxValue;
            string? bestName = null;
            foreach (var anchor in catalog)
            {
                var (px, py) = alignment.Project(anchor.Lat, anchor.Lng);
                var distance = Distance(px, py, x, y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestName = anchor.Name;
            }

            if (bestName is not null && bestDistance <= tolerance) names.Add(bestName);
        }

        return names;
    }

    private static double NearestProjectedDistance(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        AnchorAlignment alignment,
        double x,
        double y)
    {
        var bestDistance = double.MaxValue;
        foreach (var anchor in catalog)
        {
            var (px, py) = alignment.Project(anchor.Lat, anchor.Lng);
            var distance = Distance(px, py, x, y);
            if (distance < bestDistance) bestDistance = distance;
        }

        return bestDistance;
    }

    private static (double X, double Y)? NearestHit(IReadOnlyList<(double X, double Y)> hits, double x, double y)
    {
        var bestDistance = double.MaxValue;
        (double X, double Y)? best = null;
        foreach (var hit in hits)
        {
            var distance = Distance(x, y, hit.X, hit.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = hit;
            }
        }

        return best;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) BoundingBox(IReadOnlyList<AnchorCatalogEntry> catalog)
    {
        var lngMin = double.MaxValue;
        var lngMax = double.MinValue;
        var latMin = double.MaxValue;
        var latMax = double.MinValue;
        foreach (var entry in catalog)
        {
            lngMin = Math.Min(lngMin, entry.Lng);
            lngMax = Math.Max(lngMax, entry.Lng);
            latMin = Math.Min(latMin, entry.Lat);
            latMax = Math.Max(latMax, entry.Lat);
        }

        return (lngMin, lngMax, latMin, latMax);
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) BoundingBox(IReadOnlyList<(double X, double Y)> hits)
    {
        var xMin = double.MaxValue;
        var xMax = double.MinValue;
        var yMin = double.MaxValue;
        var yMax = double.MinValue;
        foreach (var (x, y) in hits)
        {
            xMin = Math.Min(xMin, x);
            xMax = Math.Max(xMax, x);
            yMin = Math.Min(yMin, y);
            yMax = Math.Max(yMax, y);
        }

        return (xMin, xMax, yMin, yMax);
    }
}
