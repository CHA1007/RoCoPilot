using RocoPilot.Input.Interception;
using RocoPilot.Input.SendInput;

namespace RocoPilot.Input;

public static class InputDriverFactory
{
    public const string Interception = "interception";
    public const string SendInput = "sendinput";

    public static IInputDriver Create(string? backend)
    {
        return Normalize(backend) switch
        {
            "" or Interception => new InterceptionDriver(),
            SendInput => new SendInputDriver(),
            var unknown => throw new ArgumentException(
                $"未知 input.backend \"{unknown}\"：应为 {Interception} / {SendInput}", nameof(backend)),
        };
    }

    private static string Normalize(string? backend) => backend?.Trim().ToLowerInvariant() ?? string.Empty;
}
