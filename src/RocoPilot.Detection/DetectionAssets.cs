namespace RocoPilot.Detection;

internal static class DetectionAssets
{
    internal static string DefaultModelPath =>
        Path.Combine(AppContext.BaseDirectory, "assets", "models", "yolo11n-roco.onnx");
}
