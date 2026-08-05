namespace RocoPilot.Routing;

public enum RouteNodeKind
{
    Anchor,
    Playback,
    Loop,
}

public sealed class RouteNode
{
    public RouteNode(
        RouteNodeKind kind,
        string name,
        double canvasX,
        double canvasY,
        string? anchorName = null,
        string? routeName = null,
        int? maxLaps = null,
        TimeSpan? maxDuration = null,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        switch (kind)
        {
            case RouteNodeKind.Anchor when string.IsNullOrWhiteSpace(anchorName):
                throw new ArgumentException("锚点节点必须指定锚点名。", nameof(anchorName));
            case RouteNodeKind.Playback when string.IsNullOrWhiteSpace(routeName):
                throw new ArgumentException("回放节点必须指定关联路线。", nameof(routeName));
        }

        if (maxLaps is < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLaps), "圈数上限至少为 1。");
        if (maxDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDuration), "时长上限必须为正。");

        Id = id ?? Guid.NewGuid();
        Kind = kind;
        Name = name.Trim();
        CanvasX = canvasX;
        CanvasY = canvasY;
        AnchorName = anchorName;
        RouteName = routeName;
        MaxLaps = maxLaps;
        MaxDuration = maxDuration;
    }

    public Guid Id { get; }

    public RouteNodeKind Kind { get; }

    public string Name { get; }

    public double CanvasX { get; }

    public double CanvasY { get; }

    public string? AnchorName { get; }

    public string? RouteName { get; }

    public int? MaxLaps { get; }

    public TimeSpan? MaxDuration { get; }
}

public sealed record RouteEdge(Guid FromId, Guid ToId);

public sealed class RouteGraph
{
    public RouteGraph(string name, IReadOnlyList<RouteNode> nodes, IReadOnlyList<RouteEdge> edges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Edges = edges ?? throw new ArgumentNullException(nameof(edges));
    }

    public string Name { get; }

    public IReadOnlyList<RouteNode> Nodes { get; }

    public IReadOnlyList<RouteEdge> Edges { get; }

    public IReadOnlyList<RouteNode> OrderedChain()
    {
        var byId = new Dictionary<Guid, RouteNode>(Nodes.Count);
        foreach (var node in Nodes)
        {
            if (!byId.TryAdd(node.Id, node))
                throw new InvalidOperationException($"节点 Id 重复：{node.Id}");
        }

        var outNext = new Dictionary<Guid, Guid>();
        var incoming = new Dictionary<Guid, int>();
        foreach (var edge in Edges)
        {
            if (edge.FromId == edge.ToId)
                throw new InvalidOperationException("不允许自连线。");
            if (!byId.ContainsKey(edge.FromId) || !byId.ContainsKey(edge.ToId))
                throw new InvalidOperationException("连线端点不存在。");
            if (!outNext.TryAdd(edge.FromId, edge.ToId))
                throw new InvalidOperationException($"线性拓扑校验失败：节点「{byId[edge.FromId].Name}」有多条出边。");

            incoming.TryGetValue(edge.ToId, out var count);
            if (count + 1 > 1)
                throw new InvalidOperationException($"线性拓扑校验失败：节点「{byId[edge.ToId].Name}」有多条入边。");
            incoming[edge.ToId] = count + 1;
        }

        var starts = Nodes.Where(node => !incoming.ContainsKey(node.Id)).ToList();
        if (starts.Count != 1)
            throw new InvalidOperationException($"v1 仅支持单条线性链：检测到 {starts.Count} 个起点。");

        var chain = new List<RouteNode>(Nodes.Count);
        var visited = new HashSet<Guid>();
        var current = starts[0];
        while (true)
        {
            if (!visited.Add(current.Id))
                throw new InvalidOperationException("图中存在环（循环语义请用循环节点表达）。");

            chain.Add(current);
            if (!outNext.TryGetValue(current.Id, out var nextId))
                break;

            current = byId[nextId];
        }

        if (chain.Count != Nodes.Count)
            throw new InvalidOperationException("图中存在未连入主链的孤立节点。");

        return chain;
    }
}
