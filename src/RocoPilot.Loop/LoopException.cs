namespace RocoPilot.Loop;

public class LoopException : Exception
{
    public LoopException(string message) : base(message) { }

    public LoopException(string message, Exception innerException) : base(message, innerException) { }
}
