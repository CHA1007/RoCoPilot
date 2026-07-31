using System.Runtime.InteropServices;

namespace RocoPilot.Input.Interception;

internal interface IInterceptionApi
{
    IntPtr CreateContext();

    void DestroyContext(IntPtr context);

    void SetFilter(IntPtr context, int device, ushort filterMask);

    int WaitWithTimeout(IntPtr context, uint timeoutMs);

    int ReceiveMouseStroke(IntPtr context, int device, out InterceptionMouseStroke stroke);

    int SendMouseStroke(IntPtr context, int device, in InterceptionMouseStroke stroke);

    int ReceiveKeyStroke(IntPtr context, int device, out InterceptionKeyStroke stroke);

    int SendKeyStroke(IntPtr context, int device, in InterceptionKeyStroke stroke);
}

[StructLayout(LayoutKind.Sequential)]
internal struct InterceptionMouseStroke
{
    public ushort State;
    public ushort Flags;
    public short Rolling;
    public int X;
    public int Y;
    public uint Information;
}

[StructLayout(LayoutKind.Sequential)]
internal struct InterceptionKeyStroke
{
    public ushort Code;
    public ushort State;
    public uint Information;
}

internal static class InterceptionConstants
{
    public const int KeyboardDeviceMin = 1;
    public const int KeyboardDeviceMax = 10;
    public const int MouseDeviceMin = 11;
    public const int MouseDeviceMax = 20;

    public const ushort FilterAll = 0xFFFF;
    public const ushort FilterNone = 0x0000;

    public const ushort MouseLeftDown = 0x001;
    public const ushort MouseLeftUp = 0x002;
    public const ushort MouseRightDown = 0x004;
    public const ushort MouseRightUp = 0x008;
    public const ushort MouseMiddleDown = 0x010;
    public const ushort MouseMiddleUp = 0x020;

    public const ushort KeyUp = 0x001;
}
