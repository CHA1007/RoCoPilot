namespace RocoPilot.Detection;

public interface IDetector
{
    string BackendName { get; }

    IReadOnlyList<string> ClassNames { get; }

    IReadOnlyList<DetectedBox> Detect(ReadOnlySpan<byte> bgraPixels, int width, int height);
}

public static class DetectionBackends
{
    public const string OnnxYolo = "onnx-yolo";
}
