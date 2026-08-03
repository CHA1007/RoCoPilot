using Microsoft.Win32;

namespace RocoPilot.Loop;

internal static class MouseAccelerationProbe
{
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            var speed = key?.GetValue("MouseSpeed") as string;
            return speed is "1" or "2";
        }
        catch
        {

            return false;
        }
    }
}
