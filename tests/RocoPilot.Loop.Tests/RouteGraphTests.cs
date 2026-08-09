using RocoPilot.Routing;

namespace RocoPilot.Loop.Tests;

public class RouteGraphTests
{
    [Fact]
    public void ChainPreservesNodeOrder()
    {
        var (a, b, c) = Chain3();
        var graph = new RouteGraph("g", [a, b, c]);

        var ordered = graph.OrderedChain();

        Assert.False(ordered.LoopsToHead);
        Assert.Equal([a.Id, b.Id, c.Id], ordered.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void LoopsToHeadFlagPropagatesToOrderedChain()
    {
        var (a, b, c) = Chain3();
        var graph = new RouteGraph("g", [a, b, c], loopsToHead: true);

        var ordered = graph.OrderedChain();

        Assert.True(ordered.LoopsToHead);
        Assert.Equal([a.Id, b.Id, c.Id], ordered.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void EmptyGraphIsRejected()
    {
        var graph = new RouteGraph("g", []);

        Assert.Throws<InvalidOperationException>(() => graph.OrderedChain());
    }

    [Fact]
    public void DuplicateNodeIdIsRejected()
    {
        var a = Node("a");
        var duplicate = new TeleportNode("传送·b", "b", a.Id);
        var graph = new RouteGraph("g", [a, duplicate]);

        Assert.Throws<InvalidOperationException>(() => graph.OrderedChain());
    }

    [Fact]
    public void LoopCapsRoundTrip()
    {
        var graph = new RouteGraph("g", [Node("a")], loopsToHead: true, maxLaps: 3, maxDuration: TimeSpan.FromMinutes(30));

        Assert.Equal(3, graph.MaxLaps);
        Assert.Equal(TimeSpan.FromMinutes(30), graph.MaxDuration);
    }

    [Fact]
    public void MaxLapsBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RouteGraph("g", [Node("a")], loopsToHead: true, maxLaps: 0));
    }

    [Fact]
    public void NonPositiveMaxDurationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RouteGraph("g", [Node("a")], loopsToHead: true, maxDuration: TimeSpan.Zero));
    }

    private static ActionNode Node(string anchor) => new TeleportNode($"传送·{anchor}", anchor);

    private static (ActionNode A, ActionNode B, ActionNode C) Chain3() => (Node("a"), Node("b"), Node("c"));
}
