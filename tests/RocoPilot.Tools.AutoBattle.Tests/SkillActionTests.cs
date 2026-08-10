using RocoPilot.Input;
using RocoPilot.Tools.AutoBattle.Battle;

namespace RocoPilot.Tools.AutoBattle.Tests;

public class SkillActionTests
{
    [Fact]
    public void EmitsRThenSelectedSkillKey()
    {
        var driver = new RecordingDriver();
        var settings = new AutoBattleSettings { SkillSlot = 2, ClickDelayMinMs = 50, ClickDelayMaxMs = 50 };
        var action = new SkillAction(() => settings);

        action.Execute(driver, null!, default, 0, 0);

        Assert.Equal(new ushort[] { 0x52, 0x32 }, driver.MouseAndKeys);
    }

    [Fact]
    public void UsesUpdatedSlotOnNextExecution()
    {
        var driver = new RecordingDriver();
        var settings = new AutoBattleSettings { SkillSlot = 1, ClickDelayMinMs = 50, ClickDelayMaxMs = 50 };
        var action = new SkillAction(() => settings);

        action.Execute(driver, null!, default, 0, 0);
        settings.SkillSlot = 3;
        action.Execute(driver, null!, default, 0, 0);

        Assert.Equal(new ushort[] { 0x52, 0x31, 0x52, 0x33 }, driver.MouseAndKeys);
    }

    [Fact]
    public void ClampsSkillSlotToRange()
    {
        var driver = new RecordingDriver();
        var action = new SkillAction(() => new AutoBattleSettings { SkillSlot = 9, ClickDelayMinMs = 50, ClickDelayMaxMs = 50 });

        action.Execute(driver, null!, default, 0, 0);

        Assert.Equal(new ushort[] { 0x52, 0x34 }, driver.MouseAndKeys);
    }

    [Fact]
    public void UsesDefaultsWhenProviderReturnsNull()
    {
        var driver = new RecordingDriver();
        var action = new SkillAction(() => null!);

        action.Execute(driver, null!, default, 0, 0);

        Assert.Equal(new ushort[] { 0x52, 0x31 }, driver.MouseAndKeys);
    }

    private sealed class RecordingDriver : IInputDriver
    {
        public List<ushort> MouseAndKeys { get; } = [];

        public string BackendName => "recording";

        public void Arm() { }

        public void MoveRelative(int dx, int dy) { }

        public void KeyDown(InputKey key) => MouseAndKeys.Add(key.VirtualKey);

        public void KeyUp(InputKey key) { }

        public void SendRawStroke(ReceivedStroke stroke) { }

        public void StartStrokeRelay(Action<ReceivedStroke> onStroke) { }

        public void StopStrokeRelay() { }

        public void Dispose() { }
    }
}