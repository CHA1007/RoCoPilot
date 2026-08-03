namespace RocoPilot.Input;

public static class InputDriverExtensions
{
    public static void KeyPress(this IInputDriver driver, InputKey key, int holdMs = 50) =>
        MacroRunner.Run(driver, [MacroStep.Press(key, holdMs)]);

    public static void RunMacro(this IInputDriver driver, IReadOnlyList<MacroStep> steps) =>
        MacroRunner.Run(driver, steps);

    public static void ClickAt(this IInputDriver driver, int screenX, int screenY, int holdMs = 50)
    {
        RocoPilot.Input.Native.User32.GetCursorPos(out var pos);
        double invX = 1, invY = 1;

        for (var i = 0; i < 5; i++)
        {
            var remX = screenX - pos.X;
            var remY = screenY - pos.Y;
            if (Math.Abs(remX) <= 2 && Math.Abs(remY) <= 2) break;

            var dx = (int)Math.Round(remX * invX);
            var dy = (int)Math.Round(remY * invY);
            driver.MoveRelative(dx, dy);
            Thread.Sleep(30);

            RocoPilot.Input.Native.User32.GetCursorPos(out var now);
            var ax = now.X - pos.X;
            var ay = now.Y - pos.Y;

            if (Math.Abs(ax) >= 4) invX = (double)dx / ax;
            if (Math.Abs(ay) >= 4) invY = (double)dy / ay;
            pos = now;
        }

        driver.KeyPress(InputKey.LeftMouse, holdMs);
    }
}
