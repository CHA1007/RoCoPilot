using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RocoPilot.Routing;

public sealed record AnchorCatalogEntry(string Name, string Layer, double Lat, double Lng);

public static class AnchorCatalog
{
    public const string GroundLayer = "G";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<IReadOnlyList<AnchorCatalogEntry>> Loaded = new(LoadEmbeddedCore);

    public static IReadOnlyList<AnchorCatalogEntry> Entries => Loaded.Value;

    public static IReadOnlyList<AnchorCatalogEntry> GroundEntries =>
        Entries.Where(entry => entry.Layer == GroundLayer).ToList();

    private static IReadOnlyList<AnchorCatalogEntry> LoadEmbeddedCore()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RocoPilot.Routing.Data.anchor-catalog.json")
            ?? throw new InvalidOperationException("内置魔力之源目录缺失");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<List<AnchorCatalogEntry>>(reader.ReadToEnd(), JsonOptions) ?? [];
    }
}
