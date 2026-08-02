namespace RocoPilot.Input;

public static class InputDriverExtensions
{
    public static void KeyPress(this IInputDriver driver, InputKey key, int holdMs = 50) =>
        MacroRunner.Run(driver, [MacroStep.Press(key, holdMs)]);

    public static void RunMacro(this IInputDriver driver, IReadOnlyList<MacroStep> steps) =>
        MacroRunner.Run(driver, steps);

    /// <summary>将光标移动到屏幕绝对坐标并左键单击。</summary>
    public static void ClickAt(this IInputDriver driver, int screenX, int screenY, int holdMs = 50)
    {
        RocoPilot.Input.Native.User32.GetCursorPos(out var pos);
        driver.MoveRelative(screenX - pos.X, screenY - pos.Y);
        Thread.Sleep(30);
        driver.KeyPress(InputKey.LeftMouse, holdMs);
    }
}
