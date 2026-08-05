using System.Runtime.InteropServices;

namespace RocoPilot.Input.Interception;

internal sealed class InterceptionNativeApi : IInterceptionApi
{
    private const string Lib = "interception.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InterceptionPredicate(int device);

    private readonly Dictionary<IntPtr, List<InterceptionPredicate>> _predicatesByContext = [];

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr interception_create_context();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, int device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_wait_with_timeout(IntPtr context, uint milliseconds);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_receive")]
    private static extern int interception_receive_mouse(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    private static extern int interception_send_mouse(IntPtr context, int device, in InterceptionMouseStroke stroke, uint nstroke);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_receive")]
    private static extern int interception_receive_key(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    private static extern int interception_send_key(IntPtr context, int device, in InterceptionKeyStroke stroke, uint nstroke);

    public IntPtr CreateContext() => interception_create_context();

    public void DestroyContext(IntPtr context)
    {
        interception_destroy_context(context);
        _predicatesByContext.Remove(context);
    }

    public void SetFilter(IntPtr context, int device, ushort filterMask)
    {
        InterceptionPredicate predicate = _ => filterMask;
        if (!_predicatesByContext.TryGetValue(context, out var predicates))
        {
            predicates = [];
            _predicatesByContext[context] = predicates;
        }

        predicates.Add(predicate);
        interception_set_filter(context, predicate, device);
    }

    public int WaitWithTimeout(IntPtr context, uint timeoutMs) => interception_wait_with_timeout(context, timeoutMs);

    public int ReceiveMouseStroke(IntPtr context, int device, out InterceptionMouseStroke stroke)
    {
        stroke = default;
        return interception_receive_mouse(context, device, ref stroke, nstroke: 1);
    }

    public int SendMouseStroke(IntPtr context, int device, in InterceptionMouseStroke stroke) =>
        interception_send_mouse(context, device, in stroke, nstroke: 1);

    public int ReceiveKeyStroke(IntPtr context, int device, out InterceptionKeyStroke stroke)
    {
        stroke = default;
        return interception_receive_key(context, device, ref stroke, nstroke: 1);
    }

    public int SendKeyStroke(IntPtr context, int device, in InterceptionKeyStroke stroke) =>
        interception_send_key(context, device, in stroke, nstroke: 1);
}
