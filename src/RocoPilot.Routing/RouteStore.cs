using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            null,
            graph.LoopsToHead,
            graph.MaxLaps,
            graph.MaxDuration);

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

        var nodes = new List<NodeRecord>(file.Nodes);
        var loopsToHead = file.LoopsToHead;
        var maxLaps = file.MaxLaps;
        var maxDuration = file.MaxDuration;

        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].Kind != LegacyLoopKind) continue;
            maxLaps ??= nodes[i].MaxLaps;
            maxDuration ??= nodes[i].MaxDuration;
            nodes.RemoveAt(i);
            loopsToHead = true;
        }

        if (file.Edges is { Count: > 0 })
            nodes = OrderByLegacyEdges(nodes, file.Edges, ref loopsToHead);

        var graph = new RouteGraph(
            file.Name,
            [.. nodes.Select(FromNodeRecord)],
            loopsToHead,
            maxLaps,
            maxDuration);

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

    private static List<NodeRecord> OrderByLegacyEdges(List<NodeRecord> nodes, List<EdgeRecord> edges, ref bool loopsToHead)
    {
        var byId = nodes.ToDictionary(node => node.Id);
        var next = new Dictionary<Guid, Guid>();
        var incoming = new Dictionary<Guid, int>();

        foreach (var edge in edges)
        {
            if (edge.FromId == edge.ToId)
                throw new InvalidDataException("执行图无效：不允许自连线。");
            if (!byId.ContainsKey(edge.FromId) || !byId.ContainsKey(edge.ToId))
                throw new InvalidDataException("执行图无效：连线端点不存在。");
            if (!next.TryAdd(edge.FromId, edge.ToId))
                throw new InvalidDataException($"执行图无效：节点「{byId[edge.FromId].Name}」有多条出边。");
            incoming.TryGetValue(edge.ToId, out var count);
            incoming[edge.ToId] = count + 1;
        }

        foreach (var (toId, count) in incoming)
        {
            if (count > 1)
                throw new InvalidDataException($"执行图无效：节点「{byId[toId].Name}」有多条入边。");
        }

        if (!loopsToHead && nodes.Count > 1)
        {
            for (var i = edges.Count - 1; i >= 0; i--)
            {
                if (LegacyCycleSize(next, edges[i]) != nodes.Count) continue;
                next.Remove(edges[i].FromId);
                incoming.Remove(edges[i].ToId);
                loopsToHead = true;
                break;
            }
        }

        var starts = nodes.Where(node => !incoming.ContainsKey(node.Id)).ToList();
        if (starts.Count != 1)
            throw new InvalidDataException($"执行图无效：检测到 {starts.Count} 个起点。");

        var ordered = new List<NodeRecord>(nodes.Count);
        var current = starts[0].Id;
        while (true)
        {
            ordered.Add(byId[current]);
            if (!next.TryGetValue(current, out var nextId)) break;
            current = nextId;
        }

        if (ordered.Count != nodes.Count)
            throw new InvalidDataException("执行图无效：存在未连入主链的孤立节点。");

        return ordered;
    }

    private static int LegacyCycleSize(IReadOnlyDictionary<Guid, Guid> next, EdgeRecord edge)
    {
        var size = 1;
        var current = edge.ToId;
        while (current != edge.FromId)
        {
            if (!next.TryGetValue(current, out var nextId)) return 0;
            current = nextId;
            size++;
        }

        return size;
    }

    private static NodeRecord ToNodeRecord(RouteNode node) => new(
        node.Id,
        node.Kind.ToString(),
        node.Name,
        node.AnchorName,
        node.RouteName,
        null,
        null);

    private static RouteNode FromNodeRecord(NodeRecord record) => new(
        Enum.Parse<RouteNodeKind>(record.Kind),
        record.Name,
        record.AnchorName,
        record.RouteName,
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

    private const string LegacyLoopKind = "Loop";

    private sealed record GraphFile(
        string Name,
        List<NodeRecord> Nodes,
        List<EdgeRecord>? Edges,
        bool LoopsToHead = false,
        int? MaxLaps = null,
        TimeSpan? MaxDuration = null);

    private sealed record EdgeRecord(Guid FromId, Guid ToId);

    private sealed record NodeRecord(
        Guid Id,
        [property: JsonConverter(typeof(NodeKindJsonConverter))] string Kind,
        string Name,
        string? AnchorName,
        string? RouteName,
        int? MaxLaps,
        TimeSpan? MaxDuration);

    private sealed class NodeKindJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Number ? LegacyKindName(reader.GetInt32()) : reader.GetString() ?? string.Empty;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);

        private static string LegacyKindName(int value) => value switch
        {
            0 => "Anchor",
            1 => "Playback",
            2 => LegacyLoopKind,
            _ => throw new InvalidDataException($"未知节点类型：{value}"),
        };
    }
}
