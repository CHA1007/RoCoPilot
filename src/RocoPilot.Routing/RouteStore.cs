using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RocoPilot.Settings;

namespace RocoPilot.Routing;

public sealed class RouteStore
{
    private const string GraphFileName = "graph.json";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _routesRoot;

    public RouteStore(string? routesRoot = null) => _routesRoot = routesRoot ?? RocoPaths.RoutesRoot;

    public string RoutesRoot => _routesRoot;

    public async Task SaveGraphAsync(RouteGraph graph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        Directory.CreateDirectory(_routesRoot);
        var file = new GraphFile(
            graph.Name,
            [.. graph.Nodes],
            graph.LoopsToHead,
            graph.MaxLaps,
            graph.MaxDuration);

        var jsonPath = Path.Combine(_routesRoot, GraphFileName);
        await using var stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
    }

    public async Task<RouteGraph> LoadGraphAsync(CancellationToken cancellationToken = default)
    {
        var jsonPath = Path.Combine(_routesRoot, GraphFileName);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("路线执行图不存在。", jsonPath);

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);

        List<ActionNode> nodes;
        string name;
        bool loopsToHead;
        int? maxLaps;
        TimeSpan? maxDuration;

        if (TryDeserializeNewFormat(json, out var file))
        {
            name = file.Name;
            nodes = file.Nodes.Where(node => node is not null).ToList();
            loopsToHead = file.LoopsToHead;
            maxLaps = file.MaxLaps;
            maxDuration = file.MaxDuration;
        }
        else if (TryDeserializeLegacyFormat(json, out var legacy))
        {
            // 旧格式：扁平 NodeRecord（无 kind 鉴别器），全部视为传送节点
            name = legacy.Name;
            nodes = legacy.Nodes
                .Where(record => !string.IsNullOrWhiteSpace(record.AnchorName))
                .Select(record => (ActionNode)new TeleportNode(record.Name, record.AnchorName!, record.Id))
                .ToList();
            loopsToHead = legacy.LoopsToHead;
            maxLaps = legacy.MaxLaps;
            maxDuration = legacy.MaxDuration;
        }
        else
        {
            throw new InvalidDataException($"执行图文件损坏：{jsonPath}");
        }

        var graph = new RouteGraph(name, nodes, loopsToHead, maxLaps, maxDuration);

        if (graph.Nodes.Count == 0)
            throw new FileNotFoundException("路线执行图不含有效步骤。", jsonPath);

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

    private static bool TryDeserializeNewFormat(string json, out GraphFile file)
    {
        try
        {
            file = JsonSerializer.Deserialize<GraphFile>(json, JsonOptions) ?? throw new InvalidDataException("空图");
            return true;
        }
        catch (JsonException)
        {
            file = null!;
            return false;
        }
        catch (NotSupportedException)
        {
            // 多态类型缺鉴别器（旧数据）时 System.Text.Json 抛此异常而非 JsonException
            file = null!;
            return false;
        }
    }

    private static bool TryDeserializeLegacyFormat(string json, out LegacyGraphFile file)
    {
        try
        {
            file = JsonSerializer.Deserialize<LegacyGraphFile>(json, JsonOptions) ?? throw new InvalidDataException("空图");
            return true;
        }
        catch (JsonException)
        {
            file = null!;
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record GraphFile(
        string Name,
        List<ActionNode> Nodes,
        bool LoopsToHead = false,
        int? MaxLaps = null,
        TimeSpan? MaxDuration = null);

    private sealed record LegacyGraphFile(
        string Name,
        List<NodeRecord> Nodes,
        bool LoopsToHead = false,
        int? MaxLaps = null,
        TimeSpan? MaxDuration = null);

    private sealed record NodeRecord(Guid Id, string Name, string? AnchorName);
}