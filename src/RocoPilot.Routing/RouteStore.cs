using System.Text.Json;
using RocoPilot.Settings;

namespace RocoPilot.Routing;

public sealed class RouteStore
{
    private const string RouteFileName = "route.json";
    private const string GraphFileName = "graph.json";
    private const string KeyframesDirectoryName = "keyframes";

    private readonly string _routesRoot;

    public RouteStore(string? routesRoot = null) => _routesRoot = routesRoot ?? RocoPaths.RoutesRoot;

    public string RoutesRoot => _routesRoot;

    public async Task SaveAsync(Route route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        var directory = RouteDirectory(route.Name);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(Path.Combine(directory, KeyframesDirectoryName));

        var keyframeEntries = new List<KeyframeEntry>(route.Keyframes.Count);
        for (var i = 0; i < route.Keyframes.Count; i++)
        {
            var keyframe = route.Keyframes[i];
            var fileName = $"{i:0000}.png";
            await File.WriteAllBytesAsync(
                Path.Combine(directory, KeyframesDirectoryName, fileName), keyframe.MinimapPng, cancellationToken);
            keyframeEntries.Add(new KeyframeEntry(keyframe.OffsetMs, fileName, keyframe.Width, keyframe.Height));
        }

        var file = new RouteFile(route.Name, route.RecordedAt, route.Duration, [.. route.Events], keyframeEntries);
        var jsonPath = Path.Combine(directory, RouteFileName);
        await using var stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, file, cancellationToken: cancellationToken);
    }

    public async Task<Route> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var directory = RouteDirectory(name);
        var jsonPath = Path.Combine(directory, RouteFileName);
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"路线「{name}」不存在。", jsonPath);
        }

        await using var stream = File.OpenRead(jsonPath);
        var file = await JsonSerializer.DeserializeAsync<RouteFile>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"路线文件损坏：{jsonPath}");

        var keyframes = new List<RouteKeyframe>(file.Keyframes.Count);
        foreach (var entry in file.Keyframes)
        {
            var png = await File.ReadAllBytesAsync(
                Path.Combine(directory, KeyframesDirectoryName, entry.File), cancellationToken);
            keyframes.Add(new RouteKeyframe(entry.OffsetMs, entry.Width, entry.Height, png));
        }

        return new Route(file.Name, file.RecordedAt, file.Duration, file.Events, keyframes);
    }

    public async Task<IReadOnlyList<RouteSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_routesRoot)) return [];

        var summaries = new List<RouteSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_routesRoot))
        {
            var jsonPath = Path.Combine(directory, RouteFileName);
            if (!File.Exists(jsonPath)) continue;

            RouteFile? file;
            try
            {
                await using var stream = File.OpenRead(jsonPath);
                file = await JsonSerializer.DeserializeAsync<RouteFile>(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                continue;
            }

            if (file is not null)
            {
                summaries.Add(new RouteSummary(file.Name, file.RecordedAt, file.Duration, file.Events.Count));
            }
        }

        return summaries.OrderByDescending(summary => summary.RecordedAt).ToList();
    }

    public async Task SaveGraphAsync(RouteGraph graph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        Directory.CreateDirectory(_routesRoot);
        var file = new GraphFile(
            graph.Name,
            [.. graph.Nodes.Select(ToNodeRecord)],
            [.. graph.Edges]);

        var jsonPath = Path.Combine(_routesRoot, GraphFileName);
        await using var stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, file, cancellationToken: cancellationToken);
    }

    public async Task<RouteGraph> LoadGraphAsync(CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.Combine(_routesRoot, GraphFileName);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("路线执行图不存在。", jsonPath);

        await using var stream = File.OpenRead(jsonPath);
        var file = await JsonSerializer.DeserializeAsync<GraphFile>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"执行图文件损坏：{jsonPath}");

        var graph = new RouteGraph(
            file.Name,
            [.. file.Nodes.Select(FromNodeRecord)],
            [.. file.Edges]);

        try
        {
            graph.OrderedChain();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"执行图无效：{ex.Message}", ex);
        }

        return graph;
    }

    private static NodeRecord ToNodeRecord(RouteNode node) => new(
        node.Id,
        node.Kind,
        node.Name,
        node.CanvasX,
        node.CanvasY,
        node.PoiName,
        node.RouteName,
        node.MaxLaps,
        node.MaxDuration);

    private static RouteNode FromNodeRecord(NodeRecord record) => new(
        record.Kind,
        record.Name,
        record.CanvasX,
        record.CanvasY,
        record.PoiName,
        record.RouteName,
        record.MaxLaps,
        record.MaxDuration,
        record.Id);

    private string RouteDirectory(string name) => Path.Combine(_routesRoot, SanitizeFolderName(name));

    internal static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return sanitized.Length > 0 ? sanitized : "route";
    }

    private sealed record RouteFile(
        string Name,
        DateTimeOffset RecordedAt,
        TimeSpan Duration,
        List<RouteEvent> Events,
        List<KeyframeEntry> Keyframes);

    private sealed record KeyframeEntry(double OffsetMs, string File, int Width, int Height);

    private sealed record GraphFile(string Name, List<NodeRecord> Nodes, List<RouteEdge> Edges);

    private sealed record NodeRecord(
        Guid Id,
        RouteNodeKind Kind,
        string Name,
        double CanvasX,
        double CanvasY,
        string? PoiName,
        string? RouteName,
        int? MaxLaps,
        TimeSpan? MaxDuration);
}
