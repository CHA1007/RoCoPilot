using RocoPilot.Detection.Inference;

namespace RocoPilot.Detection;

public static class DetectorFactory
{
    public static OnnxYoloDetector CreateOnnxYolo(DetectionOptions? options = null, string? modelPath = null, bool useGpu = false)
    {
        var path = modelPath ?? DetectionAssets.DefaultModelPath;
        var inference = new OnnxYoloInference(path, useGpu);
        try
        {
            return new OnnxYoloDetector(inference, options ?? new DetectionOptions());
        }
        catch
        {
            inference.Dispose();
            throw;
        }
    }
}
