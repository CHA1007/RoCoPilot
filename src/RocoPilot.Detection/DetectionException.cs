namespace RocoPilot.Detection;

public sealed class DetectionException : Exception
{
    public DetectionException(string message) : base(message) { }

    public DetectionException(string message, Exception innerException) : base(message, innerException) { }
}
