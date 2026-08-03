using RocoPilot.Input;

namespace RocoPilot.Tools.AutoBattle.Battle;

public interface IBattleAction
{
    bool Execute(IInputDriver driver, IBattleSensor sensor, ReadOnlySpan<byte> bgraPixels, int width, int height);
}
