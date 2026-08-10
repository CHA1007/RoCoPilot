using RocoPilot.Core;
using RocoPilot.Shell.Overlay;

namespace RocoPilot.Shell.Tests;

public class OverlayProjectionTests
{
    private static OverlayProjection RunningRoute()
    {
        var projection = new OverlayProjection(nowMs: () => 0);
        projection.ApplyEvent(new ToolEvent("route_started", new Dictionary<string, object?>()));
        return projection;
    }

    private static string? PhaseAfter(params ToolEvent[] events)
    {
        var projection = RunningRoute();
        foreach (var toolEvent in events) projection.ApplyEvent(toolEvent);
        return projection.Snapshot().Phase;
    }

    [Fact]
    public void TeleportStagesProjectStepByStep()
    {
        Assert.Equal("锚点·独角兽领地的魔力之源", PhaseAfter(
            new ToolEvent("node_started", new Dictionary<string, object?>
            {
                ["node"] = "锚点·独角兽领地的魔力之源",
            })));

        Assert.Equal("传送·开图", PhaseAfter(
            new ToolEvent("map_open", new Dictionary<string, object?> { ["key"] = "M" })));

        Assert.Equal("传送·地图校准", PhaseAfter(
            new ToolEvent("map_calibrate", new Dictionary<string, object?> { ["phase"] = "homeland_opened" })));

        Assert.Equal("传送·缩到最小", PhaseAfter(
            new ToolEvent("map_calibrate", new Dictionary<string, object?> { ["phase"] = "zoom_out" })));

        Assert.Equal("传送·定位锚点（内点 31）", PhaseAfter(
            new ToolEvent("anchor_alignment", new Dictionary<string, object?> { ["inliers"] = 31, ["hits"] = 33 })));

        Assert.Equal("传送·点击锚点", PhaseAfter(
            new ToolEvent("poi_click", new Dictionary<string, object?> { ["anchor"] = "x", ["x"] = 1, ["y"] = 2 })));

        Assert.Equal("传送·等待落地", PhaseAfter(
            new ToolEvent("teleport_clicked", new Dictionary<string, object?>())));

        Assert.Equal("传送·已落地", PhaseAfter(
            new ToolEvent("anchor_teleport", new Dictionary<string, object?> { ["phase"] = "landed" })));
    }

    [Fact]
    public void FastTravelLandedClearsWaitingPhase()
    {
        var phase = PhaseAfter(
            new ToolEvent("teleport_clicked", new Dictionary<string, object?>()),
            new ToolEvent("fast_travel_landed", new Dictionary<string, object?>()));

        Assert.Null(phase);
    }

    [Fact]
    public void TeleportFailureProjectsFailurePhase()
    {
        Assert.Equal("传送·失败", PhaseAfter(
            new ToolEvent("anchor_failed", new Dictionary<string, object?>
            {
                ["failure"] = "MapNotConfirmed",
                ["message"] = "x",
            })));
    }

    [Fact]
    public void LapPrefixAppliesToTeleportStages()
    {
        Assert.Equal("第2圈｜传送·开图", PhaseAfter(
            new ToolEvent("loop_lap", new Dictionary<string, object?> { ["lap"] = 2 }),
            new ToolEvent("map_open", new Dictionary<string, object?> { ["key"] = "M" })));
    }

    [Fact]
    public void GraphFinishedClearsPhase()
    {
        var phase = PhaseAfter(
            new ToolEvent("map_open", new Dictionary<string, object?> { ["key"] = "M" }),
            new ToolEvent("graph_finished", new Dictionary<string, object?> { ["reason"] = "Completed" }));

        Assert.Null(phase);
    }
}