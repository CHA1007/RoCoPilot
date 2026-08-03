using RocoPilot.Input;

namespace RocoPilot.Tools.AutoBattle.Battle;

public sealed class SkillAction : IBattleAction
{
    private readonly int _skillSlot;
    private readonly Random _random = new();
    private readonly int _clickDelayMinMs;
    private readonly int _clickDelayMaxMs;

    public SkillAction(int skillSlot, int clickDelayMinMs = 200, int clickDelayMaxMs = 500)
    {
        _skillSlot = Math.Clamp(skillSlot, 1, 4);
        _clickDelayMinMs = Math.Max(50, clickDelayMinMs);
        _clickDelayMaxMs = Math.Max(_clickDelayMinMs, clickDelayMaxMs);
    }

    public bool Execute(IInputDriver driver, IBattleSensor sensor, ReadOnlySpan<byte> bgraPixels, int width, int height)
    {

        driver.KeyPress(InputKey.Keyboard(0x52));
        Thread.Sleep(RandomDelay());

        var key = (ushort)(0x30 + _skillSlot);
        driver.KeyPress(InputKey.Keyboard(key));
        Thread.Sleep(RandomDelay());

        return true;
    }

    private int RandomDelay() => _random.Next(_clickDelayMinMs, _clickDelayMaxMs + 1);
}
