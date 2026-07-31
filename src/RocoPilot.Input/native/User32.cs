using System.Runtime.InteropServices;

namespace RocoPilot.Input.Native;

internal static class User32
{
    private const uint MapvkVkToVsc = 0;

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW", SetLastError = false)]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    public static ushort MapVirtualKeyToScan(ushort virtualKey) =>
        (ushort)MapVirtualKeyW(virtualKey, MapvkVkToVsc);
}
