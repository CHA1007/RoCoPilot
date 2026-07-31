namespace RocoPilot.Input;

public enum MacroStepKind
{
    Press,

    Hold,

    Release,

    Wait,
}

public sealed record MacroStep(MacroStepKind Kind, InputKey Key = default, int Milliseconds = 0)
{
    public static MacroStep Press(InputKey key, int ms = 50) => new(MacroStepKind.Press, key, ms);
    public static MacroStep Hold(InputKey key, int ms = 500) => new(MacroStepKind.Hold, key, ms);
    public static MacroStep Release(InputKey key) => new(MacroStepKind.Release, key);
    public static MacroStep Wait(int ms = 100) => new(MacroStepKind.Wait, Milliseconds: ms);
}
