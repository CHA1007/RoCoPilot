using RocoPilot.Input;

namespace RocoPilot.Tools.AutoBattle.Battle;

public sealed class SkillAction : IBattleAction
{
    private readonly Func<AutoBattleSettings> _settingsProvider;
    private readonly Random _random = new();

    public SkillAction(Func<AutoBattleSettings> settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    public bool Execute(IInputDriver driver, IBattleSensor sensor, ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        var (skillSlot, clickDelayMinMs, clickDelayMaxMs) = CurrentTiming();
        driver.KeyPress(InputKey.Keyboard(0x52));
        Thread.Sleep(RandomDelay(clickDelayMinMs, clickDelayMaxMs));

        var key = (ushort)(0x30 + skillSlot);
        driver.KeyPress(InputKey.Keyboard(key));
        Thread.Sleep(RandomDelay(clickDelayMinMs, clickDelayMaxMs));

        return true;
    }

    private (int SkillSlot, int ClickDelayMinMs, int ClickDelayMaxMs) CurrentTiming()
    {
        var settings = _settingsProvider() ?? new AutoBattleSettings();
        var skillSlot = Math.Clamp(settings.SkillSlot, 1, 4);
        var clickDelayMinMs = Math.Max(50, settings.ClickDelayMinMs);
        var clickDelayMaxMs = Math.Max(clickDelayMinMs, settings.ClickDelayMaxMs);
        return (skillSlot, clickDelayMinMs, clickDelayMaxMs);
    }

    private int RandomDelay(int minMs, int maxMs) => _random.Next(minMs, maxMs + 1);
}