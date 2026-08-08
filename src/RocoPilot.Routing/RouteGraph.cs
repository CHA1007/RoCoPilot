using System.Text.Json.Serialization;

namespace RocoPilot.Routing;

public enum ActionKind
{
    Teleport,
    Delay,
    ScriptReplay,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TeleportNode), "teleport")]
[JsonDerivedType(typeof(DelayNode), "delay")]
[JsonDerivedType(typeof(ScriptReplayNode), "script")]
public abstract class ActionNode
{
    protected ActionNode(string name, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id ?? Guid.NewGuid();
        Name = name.Trim();
    }

    protected ActionNode() { }

    [JsonInclude]
    public Guid Id { get; private set; }

    [JsonInclude]
    public string Name { get; private set; } = string.Empty;

    public abstract ActionKind Kind { get; }
}

public sealed class TeleportNode : ActionNode
{
    public TeleportNode(string name, string anchorName, Guid? id = null) : base(name, id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorName);
        AnchorName = anchorName.Trim();
    }

    [JsonConstructor]
    private TeleportNode() { }

    [JsonInclude]
    public string AnchorName { get; private set; } = string.Empty;

    public override ActionKind Kind => ActionKind.Teleport;
}

public sealed class DelayNode : ActionNode
{
    public DelayNode(string name, TimeSpan duration, Guid? id = null) : base(name, id)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "延时必须为正。");

        Duration = duration;
    }

    [JsonConstructor]
    private DelayNode() { }

    [JsonInclude]
    public TimeSpan Duration { get; private set; }

    public override ActionKind Kind => ActionKind.Delay;
}

public sealed class ScriptReplayNode : ActionNode
{
    public ScriptReplayNode(string name, string scriptName, Guid? id = null) : base(name, id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptName);
        ScriptName = scriptName.Trim();
    }

    [JsonConstructor]
    private ScriptReplayNode() { }

    [JsonInclude]
    public string ScriptName { get; private set; } = string.Empty;

    public override ActionKind Kind => ActionKind.ScriptReplay;
}

public sealed record OrderedRouteChain(IReadOnlyList<ActionNode> Nodes, bool LoopsToHead);

public sealed class RouteGraph
{
    public RouteGraph(
        string name,
        IReadOnlyList<ActionNode> nodes,
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

    public IReadOnlyList<ActionNode> Nodes { get; }

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