using System.IO;
using RocoPilot.Routing;

namespace RocoPilot.Loop.Tests;

public class RouteStoreTests
{
    [Fact]
    public async Task GraphRoundTripsNameOrderLoopsAndCaps()
    {
        var root = TempRoot();
        try
        {
            var store = new RouteStore(root);
            var a = new TeleportNode("传送·锚点A", "锚点A");
            var b = new TeleportNode("传送·锚点B", "锚点B");
            await store.SaveGraphAsync(new RouteGraph(
                "喷泉广场刷魔尘", [a, b], loopsToHead: true, maxLaps: 2, maxDuration: TimeSpan.FromMinutes(90)));

            var loaded = await store.LoadGraphAsync();

            Assert.Equal("喷泉广场刷魔尘", loaded.Name);
            Assert.True(loaded.LoopsToHead);
            Assert.Equal(2, loaded.MaxLaps);
            Assert.Equal(TimeSpan.FromMinutes(90), loaded.MaxDuration);
            Assert.Equal([a.Id, b.Id], loaded.Nodes.Select(node => node.Id));
            Assert.Equal("锚点A", Assert.IsType<TeleportNode>(loaded.Nodes[0]).AnchorName);
            Assert.Equal("锚点B", Assert.IsType<TeleportNode>(loaded.Nodes[1]).AnchorName);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task MixedNodeTypesRoundTrip()
    {
        var root = TempRoot();
        try
        {
            var store = new RouteStore(root);
            var teleport = new TeleportNode("传送·星罗海岸", "星罗海岸");
            var delay = new DelayNode("延时 5 秒", TimeSpan.FromSeconds(5));
            await store.SaveGraphAsync(new RouteGraph("混合行动", [teleport, delay]));

            var loaded = await store.LoadGraphAsync();

            Assert.Equal(2, loaded.Nodes.Count);
            Assert.Equal(teleport.Id, loaded.Nodes[0].Id);
            Assert.Equal(delay.Id, loaded.Nodes[1].Id);

            var loadedTeleport = Assert.IsType<TeleportNode>(loaded.Nodes[0]);
            Assert.Equal("星罗海岸", loadedTeleport.AnchorName);

            var loadedDelay = Assert.IsType<DelayNode>(loaded.Nodes[1]);
            Assert.Equal(TimeSpan.FromSeconds(5), loadedDelay.Duration);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task LegacyFlatNodeFormatMigratesToTeleportNodes()
    {
        var root = TempRoot();
        try
        {
            Directory.CreateDirectory(root);
            var legacyJson = """
                {"Name":"旧采集路线","Nodes":[
                  {"Id":"541dc3d0-13d7-4567-91b1-111a4599c9f1","Name":"传送·仪式镇的魔力之源","AnchorName":"仪式镇的魔力之源"},
                  {"Id":"0e4b59a1-3a4a-4ec7-a4c4-e38fda9207cd","Name":"传送·向向日葵海岸的魔力之源","AnchorName":"向向日葵海岸的魔力之源"}
                ],"LoopsToHead":false,"MaxLaps":null,"MaxDuration":null}
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "graph.json"), legacyJson);

            var loaded = await new RouteStore(root).LoadGraphAsync();

            Assert.Equal("旧采集路线", loaded.Name);
            Assert.Equal(2, loaded.Nodes.Count);
            var first = Assert.IsType<TeleportNode>(loaded.Nodes[0]);
            Assert.Equal("仪式镇的魔力之源", first.AnchorName);
            Assert.Equal(Guid.Parse("541dc3d0-13d7-4567-91b1-111a4599c9f1"), first.Id);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task MissingGraphFileThrowsFileNotFound()
    {
        var root = TempRoot();
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => new RouteStore(root).LoadGraphAsync());
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}