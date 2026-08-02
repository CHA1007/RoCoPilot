namespace RocoPilot.Loop;

public sealed class LoopException : Exception
{
    public LoopException(string message) : base(message) { }

    public LoopException(string message, Exception innerException) : base(message, innerException) { }
}
