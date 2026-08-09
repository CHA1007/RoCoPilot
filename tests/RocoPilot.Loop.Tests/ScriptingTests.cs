using System.IO;
using RocoPilot.Input;
using RocoPilot.Scripting;

namespace RocoPilot.Loop.Tests;

public class ScriptStoreTests
{
    [Fact]
    public async Task RoundTripsNameStrokesAndTiming()
    {
        var root = TempRoot();
        try
        {
            var store = new ScriptStore(root);
            var script = new RecordedScript("开宝箱", new List<TimedStroke>
            {
                new(ReceivedDeviceKind.Keyboard, 0x20, 0, 0, 0, 0, 0, 0),
                new(ReceivedDeviceKind.Keyboard, 0x20, 1, 0, 0, 0, 0, 120),
                new(ReceivedDeviceKind.Mouse, 0x02, 0, 0, 0, 100, 200, 260),
            });

            await store.SaveAsync(script);
            var loaded = await store.LoadAsync("开宝箱");

            Assert.NotNull(loaded);
            Assert.Equal("开宝箱", loaded!.Name);
            Assert.Equal(3, loaded.Strokes.Count);
            Assert.Equal(ReceivedDeviceKind.Keyboard, loaded.Strokes[0].Kind);
            Assert.Equal((ushort)0x20, loaded.Strokes[0].Code);
            Assert.Equal((ushort)1, loaded.Strokes[1].State);
            Assert.Equal(120, loaded.Strokes[1].OffsetMs);
            var mouse = loaded.Strokes[2];
            Assert.Equal(ReceivedDeviceKind.Mouse, mouse.Kind);
            Assert.Equal(100, mouse.X);
            Assert.Equal(200, mouse.Y);
            Assert.Equal(260, mouse.OffsetMs);
            Assert.Equal(TimeSpan.FromMilliseconds(260), loaded.Duration);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ListsSummariesAndDeletes()
    {
        var root = TempRoot();
        try
        {
            var store = new ScriptStore(root);
            await store.SaveAsync(new RecordedScript("A", [new(ReceivedDeviceKind.Keyboard, 1, 0, 0, 0, 0, 0, 0)]));
            await store.SaveAsync(new RecordedScript("B", [new(ReceivedDeviceKind.Keyboard, 2, 0, 0, 0, 0, 0, 50)]));

            Assert.Equal(["A", "B"], store.List().Select(summary => summary.Name).OrderBy(name => name));

            await store.DeleteAsync("A");

            Assert.Equal(["B"], store.List().Select(summary => summary.Name));
            Assert.Null(await store.LoadAsync("A"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ExportsAndImportsAcrossStores()
    {
        var rootA = TempRoot();
        var rootB = TempRoot();
        var exportPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var storeA = new ScriptStore(rootA);
            var storeB = new ScriptStore(rootB);
            await storeA.SaveAsync(new RecordedScript("源", [new(ReceivedDeviceKind.Mouse, 1, 0, 0, 0, 10, 20, 0)]));

            await storeA.ExportAsync("源", exportPath);
            await storeB.ImportAsync(exportPath);

            var imported = await storeB.LoadAsync("源");
            Assert.NotNull(imported);
            Assert.Single(imported!.Strokes);
            Assert.Equal(10, imported.Strokes[0].X);
        }
        finally
        {
            Cleanup(rootA);
            Cleanup(rootB);
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task MissingScriptLoadsNull()
    {
        var root = TempRoot();
        try
        {
            Assert.Null(await new ScriptStore(root).LoadAsync("不存在"));
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

public class LowLevelHookMapperTests
{
    [Fact]
    public void MapKey_DownWithoutExtended_ReturnsDownState()
    {
        var (code, state) = LowLevelHookMapper.MapKey(0x20, 0);
        Assert.Equal((ushort)0x20, code);
        Assert.Equal((ushort)0, state);
    }

    [Fact]
    public void MapKey_UpFlag_SetsUpBit()
    {
        var (_, state) = LowLevelHookMapper.MapKey(0x20, 0x80);
        Assert.Equal(LowLevelHookMapper.KeyUpState, state);
    }

    [Fact]
    public void MapKey_ExtendedFlag_SetsExtendedBit()
    {
        var (_, state) = LowLevelHookMapper.MapKey(0x39, 0x01);
        Assert.Equal(LowLevelHookMapper.KeyExtendedFlag, state);
    }

    [Fact]
    public void MapKey_UpAndExtended_CombinesBothBits()
    {
        var (_, state) = LowLevelHookMapper.MapKey(0x39, 0x81);
        Assert.Equal((ushort)(LowLevelHookMapper.KeyUpState | LowLevelHookMapper.KeyExtendedFlag), state);
    }

    [Theory]
    [InlineData(0x0201, LowLevelHookMapper.MouseLeftDown)]
    [InlineData(0x0202, LowLevelHookMapper.MouseLeftUp)]
    [InlineData(0x0204, LowLevelHookMapper.MouseRightDown)]
    [InlineData(0x0205, LowLevelHookMapper.MouseRightUp)]
    [InlineData(0x0207, LowLevelHookMapper.MouseMiddleDown)]
    [InlineData(0x0208, LowLevelHookMapper.MouseMiddleUp)]
    public void MapMouse_ButtonMessage_MapsToButtonState(ulong message, ushort expected)
    {
        var evt = LowLevelHookMapper.MapMouse(message, 0, 0, 0);
        Assert.NotNull(evt);
        Assert.Equal(HookMouseKind.Button, evt!.Value.Kind);
        Assert.Equal(expected, evt.Value.State);
    }

    [Theory]
    [InlineData(0x020B, 1ul, LowLevelHookMapper.MouseX1Down)]
    [InlineData(0x020C, 1ul, LowLevelHookMapper.MouseX1Up)]
    [InlineData(0x020B, 2ul, LowLevelHookMapper.MouseX2Down)]
    [InlineData(0x020C, 2ul, LowLevelHookMapper.MouseX2Up)]
    public void MapMouse_XButton_RespectsButtonIndex(ulong message, ulong highWord, ushort expected)
    {
        var evt = LowLevelHookMapper.MapMouse(message, 0, 0, (uint)(highWord << 16));
        Assert.NotNull(evt);
        Assert.Equal(HookMouseKind.Button, evt!.Value.Kind);
        Assert.Equal(expected, evt.Value.State);
    }

    [Fact]
    public void MapMouse_Wheel_ReportsStateAndDelta()
    {
        var evt = LowLevelHookMapper.MapMouse(0x020A, 0, 0, 0x00780000);
        Assert.NotNull(evt);
        Assert.Equal(HookMouseKind.Wheel, evt!.Value.Kind);
        Assert.Equal(LowLevelHookMapper.MouseWheel, evt.Value.State);
        Assert.Equal((short)120, evt.Value.Rolling);
    }

    [Fact]
    public void MapMouse_Move_ReportsAbsoluteCursor()
    {
        var evt = LowLevelHookMapper.MapMouse(0x0200, 320, 240, 0);
        Assert.NotNull(evt);
        Assert.Equal(HookMouseKind.Move, evt!.Value.Kind);
        Assert.Equal(320, evt.Value.X);
        Assert.Equal(240, evt.Value.Y);
    }

    [Fact]
    public void MapMouse_UnknownMessage_ReturnsNull()
    {
        Assert.Null(LowLevelHookMapper.MapMouse(0x1234, 0, 0, 0));
    }

    [Fact]
    public void RawMouseMove_Relative_UsesLastXYAsDelta()
    {
        var d = LowLevelHookMapper.RawMouseMove(0, 3, -2, 100, 100);
        Assert.Equal((3, -2), d);
    }

    [Fact]
    public void RawMouseMove_Absolute_ComputesDeltaFromPrevious()
    {
        var d = LowLevelHookMapper.RawMouseMove(LowLevelHookMapper.RiMouseMoveAbsolute, 110, 95, 100, 100);
        Assert.Equal((10, -5), d);
    }

    [Theory]
    [InlineData(LowLevelHookMapper.VkMenu, false, true)]
    [InlineData(LowLevelHookMapper.VkLWin, false, true)]
    [InlineData(LowLevelHookMapper.VkRWin, true, true)]
    [InlineData(LowLevelHookMapper.VkTab, true, true)]
    [InlineData(LowLevelHookMapper.VkTab, false, false)]
    [InlineData((uint)0x20, false, false)]
    public void IsSystemNavigationKey_FiltersModifiersAndAltTab(uint vkCode, bool altDown, bool expected)
    {
        Assert.Equal(expected, LowLevelHookMapper.IsSystemNavigationKey(vkCode, altDown));
    }
}

public class StrokeReplayerTests
{
    [Fact]
    public async Task SendsRawStrokesInOffsetOrder()
    {
        var driver = new FakeInputDriver();
        var replayer = new StrokeReplayer();
        var script = new RecordedScript("回放", new List<TimedStroke>
        {
            new(ReceivedDeviceKind.Keyboard, 0x20, 0, 0, 0, 0, 0, 0),
            new(ReceivedDeviceKind.Keyboard, 0x20, 1, 0, 0, 0, 0, 30),
            new(ReceivedDeviceKind.Mouse, 0x01, 0, 0, 0, 5, 6, 60),
        });

        await replayer.ReplayAsync(driver, script);

        Assert.Equal(3, driver.Sent.Count);
        Assert.Equal(ReceivedDeviceKind.Keyboard, driver.Sent[0].Kind);
        Assert.Equal((ushort)0x20, driver.Sent[0].Code);
        Assert.Equal(ReceivedDeviceKind.Mouse, driver.Sent[2].Kind);
        Assert.Equal(5, driver.Sent[2].X);
    }

    [Fact]
    public async Task ReleasesMouseDownThatNeverCameUp()
    {
        var driver = new FakeInputDriver();
        var replayer = new StrokeReplayer();
        var script = new RecordedScript("采集", new List<TimedStroke>
        {
            new(ReceivedDeviceKind.Mouse, 0, 0x004, 0, 0, 0, 0, 0),
        });

        await replayer.ReplayAsync(driver, script);

        Assert.Equal(2, driver.Sent.Count);
        Assert.Equal((ushort)0x004, driver.Sent[0].State);
        Assert.Equal(ReceivedDeviceKind.Mouse, driver.Sent[1].Kind);
        Assert.Equal((ushort)0x008, driver.Sent[1].State);
    }

    [Fact]
    public async Task ReleasesKeyboardDownThatNeverCameUp()
    {
        var driver = new FakeInputDriver();
        var replayer = new StrokeReplayer();
        var script = new RecordedScript("按键", new List<TimedStroke>
        {
            new(ReceivedDeviceKind.Keyboard, 0x20, 0, 0, 0, 0, 0, 0),
        });

        await replayer.ReplayAsync(driver, script);

        Assert.Equal(2, driver.Sent.Count);
        Assert.Equal((ushort)0x20, driver.Sent[0].Code);
        Assert.Equal(ReceivedDeviceKind.Keyboard, driver.Sent[1].Kind);
        Assert.Equal((ushort)0x20, driver.Sent[1].Code);
        Assert.Equal((ushort)0x001, driver.Sent[1].State);
    }
}

internal sealed class FakeInputDriver : IInputDriver
{
    public string BackendName => "fake";
    public List<ReceivedStroke> Sent { get; } = [];
    public bool RelayStarted => _observer is not null;
    private Action<ReceivedStroke>? _observer;

    public void Arm() { }
    public void MoveRelative(int dx, int dy) => Sent.Add(ReceivedStroke.Mouse(0, 0, 0, dx, dy));
    public void KeyDown(InputKey key) { }
    public void KeyUp(InputKey key) { }
    public void SendRawStroke(ReceivedStroke stroke) => Sent.Add(stroke);
    public void StartStrokeRelay(Action<ReceivedStroke> onStroke) => _observer = onStroke;
    public void StopStrokeRelay() => _observer = null;
    public void Emit(ReceivedStroke stroke) => _observer?.Invoke(stroke);
    public void Dispose() { }
}