using RocoPilot.Routing;

namespace RocoPilot.Loop.Tests;

public class AnchorMapAlignerTests
{
    private const double Tolerance = 0.02;

    private static readonly List<AnchorCatalogEntry> Catalog =
    [
        new AnchorCatalogEntry("甲的魔力之源", AnchorCatalog.GroundLayer, 100, 200),
        new AnchorCatalogEntry("乙的魔力之源", AnchorCatalog.GroundLayer, -300, 150),
        new AnchorCatalogEntry("丙的魔力之源", AnchorCatalog.GroundLayer, 50, -400),
        new AnchorCatalogEntry("丁的魔力之源", AnchorCatalog.GroundLayer, -150, -100),
        new AnchorCatalogEntry("戊的魔力之源", AnchorCatalog.GroundLayer, 250, 450),
        new AnchorCatalogEntry("己的魔力之源", AnchorCatalog.GroundLayer, -400, 300),
    ];

    [Fact]
    public void AlignsPositiveOrientationAndProjectsTarget()
    {
        var hits = Project(Catalog, scaleX: 0.001, scaleY: 0.0012, offsetX: 0.3, offsetY: 0.4);

        var alignment = AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4);

        Assert.NotNull(alignment);
        Assert.Equal(Catalog.Count, alignment!.InlierNames.Count);

        var (x, y) = alignment.Project(-300, 150);
        Assert.Equal(0.3 + 0.001 * 150, x, 5);
        Assert.Equal(0.4 + 0.0012 * -300, y, 5);
    }

    [Fact]
    public void AlignsFlippedAxes()
    {
        var hits = Project(Catalog, scaleX: -0.0008, scaleY: -0.001, offsetX: 0.7, offsetY: 0.8);

        var alignment = AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4);

        Assert.NotNull(alignment);
        Assert.Equal(Catalog.Count, alignment!.InlierNames.Count);

        var (x, y) = alignment.Project(lat: 200, lng: 100);
        Assert.Equal(0.7 - 0.0008 * 100, x, 5);
        Assert.Equal(0.8 - 0.001 * 200, y, 5);
    }

    [Fact]
    public void SurvivesMissingAndSpuriousHits()
    {
        var projected = Project(Catalog, scaleX: 0.0011, scaleY: 0.0009, offsetX: 0.2, offsetY: 0.25);
        var hits = projected.Skip(2).ToList();
        hits.Add((0.05, 0.9));
        hits.Add((0.6, 0.1));

        var alignment = AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4);

        Assert.NotNull(alignment);
        Assert.True(alignment!.InlierNames.Count >= 4);

        var target = Catalog[1];
        var (x, y) = alignment.Project(target.Lat, target.Lng);
        Assert.Equal(0.2 + 0.0011 * target.Lng, x, 3);
        Assert.Equal(0.25 + 0.0009 * target.Lat, y, 3);
    }

    [Fact]
    public void JitteredHitsStillAlign()
    {
        var random = new Random(42);
        var hits = Project(Catalog, scaleX: 0.001, scaleY: 0.001, offsetX: 0.4, offsetY: 0.5)
            .Select(hit => (hit.X + (random.NextDouble() - 0.5) * 0.01, hit.Y + (random.NextDouble() - 0.5) * 0.01))
            .ToList();

        var alignment = AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4);

        Assert.NotNull(alignment);
        Assert.True(alignment!.InlierNames.Count >= 5);
    }

    [Fact]
    public void TooFewHitsReturnsNull()
    {
        var hits = Project(Catalog, scaleX: 0.001, scaleY: 0.001, offsetX: 0.3, offsetY: 0.4).Take(2).ToList();

        Assert.Null(AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4));
    }

    [Fact]
    public void DegenerateFieldCaseWithTwoHitsReturnsNull()
    {
        var hits = new List<(double X, double Y)> { (0.4406, 0.4), (0.9667, 0.7519) };

        Assert.Null(AnchorMapAligner.Align(Catalog, hits, 0.03, minInliers: 6));
    }

    [Fact]
    public void InlierCountNeverExceedsHitCount()
    {
        var projected = Project(Catalog, scaleX: 0.001, scaleY: 0.001, offsetX: 0.3, offsetY: 0.4);
        var hits = projected.Take(5).ToList();
        hits.Add((0.9, 0.9));

        var alignment = AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 4);

        Assert.NotNull(alignment);
        Assert.True(alignment!.InlierNames.Count <= hits.Count);
    }

    [Fact]
    public void RandomScatterReturnsNull()
    {
        var random = new Random(7);
        var hits = Enumerable.Range(0, 12)
            .Select(_ => (random.NextDouble(), random.NextDouble()))
            .ToList();

        Assert.Null(AnchorMapAligner.Align(Catalog, hits, Tolerance, minInliers: 5));
    }

    private static List<(double X, double Y)> Project(
        IReadOnlyList<AnchorCatalogEntry> catalog,
        double scaleX,
        double scaleY,
        double offsetX,
        double offsetY)
        => catalog
            .Select(entry => (offsetX + scaleX * entry.Lng, offsetY + scaleY * entry.Lat))
            .ToList();
}
