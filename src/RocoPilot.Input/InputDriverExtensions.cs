namespace RocoPilot.Input;

public static class InputDriverExtensions
{
    public static void KeyPress(this IInputDriver driver, InputKey key, int holdMs = 50)
    {
        driver.KeyDown(key);
        Thread.Sleep(holdMs);
        driver.KeyUp(key);
    }

    public static void ClickAt(this IInputDriver driver, int screenX, int screenY, int holdMs = 50)
    {
        driver.MoveTo(screenX, screenY);
        driver.KeyPress(InputKey.LeftMouse, holdMs);
    }

    public static void MoveTo(this IInputDriver driver, int screenX, int screenY)
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
    }

    public static void Wheel(this IInputDriver driver, int rolling)
        => driver.SendRawStroke(ReceivedStroke.Mouse(state: 0, flags: 0, (short)rolling, x: 0, y: 0));
}
