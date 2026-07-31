namespace RocoPilot.Input;

public sealed class InputDriverException : Exception
{
    public InputDriverException(string message) : base(message) { }

    public InputDriverException(string message, Exception innerException) : base(message, innerException) { }
}
