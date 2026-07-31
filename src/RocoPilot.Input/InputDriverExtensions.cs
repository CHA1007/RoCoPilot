namespace RocoPilot.Input;

public static class InputDriverExtensions
{
    public static void KeyPress(this IInputDriver driver, InputKey key, int holdMs = 50) =>
        MacroRunner.Run(driver, [MacroStep.Press(key, holdMs)]);

    public static void RunMacro(this IInputDriver driver, IReadOnlyList<MacroStep> steps) =>
        MacroRunner.Run(driver, steps);
}
