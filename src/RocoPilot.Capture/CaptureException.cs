namespace RocoPilot.Capture;

public sealed class CaptureException : Exception
{
    public CaptureException(string message)
        : base(message)
    {
    }

    public CaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
