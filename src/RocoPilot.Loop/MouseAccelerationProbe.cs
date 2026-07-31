using Microsoft.Win32;

namespace RocoPilot.Loop;

/// <summary>票 13-D：检测 Windows Enhanced Pointer Precision（鼠标加速）是否开启。</summary>
internal static class MouseAccelerationProbe
{
    /// <summary>读 HKCU\Control Panel\Mouse\MouseSpeed；"1" 或 "2" 表示加速开启。</summary>
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
            // 注册表不可读时不阻塞，当作未知（不告警）
            return false;
        }
    }
}
