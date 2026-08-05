using RocoPilot.Input.Interception;

namespace RocoPilot.Input;

public static class InputDriverFactory
{
    public static IInputDriver Create() => new InterceptionDriver();
}
