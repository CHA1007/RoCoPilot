namespace RocoPilot.Routing;

public enum RouteNodeKind
{
    Anchor,
    Playback,
}

public sealed class RouteNode
{
    public RouteNode(
        RouteNodeKind kind,
        string name,
        string? anchorName = null,
        string? routeName = null,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id ?? Guid.NewGuid();
        Kind = kind;
        Name = name.Trim();
        AnchorName = anchorName;
        RouteName = routeName;
    }

    public Guid Id { get; }

    public RouteNodeKind Kind { get; }

    public string Name { get; }

    public string? AnchorName { get; }

    public string? RouteName { get; }
}

public sealed record OrderedRouteChain(IReadOnlyList<RouteNode> Nodes, bool LoopsToHead);

public sealed class RouteGraph
{
    public RouteGraph(
        string name,
        IReadOnlyList<RouteNode> nodes,
        bool loopsToHead = false,
        int? maxLaps = null,
        TimeSpan? maxDuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (maxLaps is < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLaps), "圈数上限至少为 1。");
        if (maxDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDuration), "时长上限必须为正。");

        Name = name.Trim();
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        LoopsToHead = loopsToHead;
        MaxLaps = maxLaps;
        MaxDuration = maxDuration;
    }

    public string Name { get; }

    public IReadOnlyList<RouteNode> Nodes { get; }

    public bool LoopsToHead { get; }

    public int? MaxLaps { get; }

    public TimeSpan? MaxDuration { get; }

    public OrderedRouteChain OrderedChain()
    {
        if (Nodes.Count == 0)
            throw new InvalidOperationException("执行链为空。");

        var seen = new HashSet<Guid>(Nodes.Count);
        foreach (var node in Nodes)
        {
            if (!seen.Add(node.Id))
                throw new InvalidOperationException($"节点 Id 重复：{node.Id}");
        }

        return new OrderedRouteChain(Nodes, LoopsToHead);
    }
}
