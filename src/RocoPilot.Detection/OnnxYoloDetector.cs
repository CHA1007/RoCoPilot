using RocoPilot.Detection.Inference;

namespace RocoPilot.Detection;

public sealed class OnnxYoloDetector : IDetector, IDisposable
{
    private readonly IYoloInference _inference;
    private readonly DetectionOptions _options;
    private readonly HashSet<string>? _whitelist;
    private readonly float[] _inputBuffer;
    private readonly float[] _outputBuffer;
    private int _disposed;

    internal OnnxYoloDetector(IYoloInference inference, DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(inference);
        ArgumentNullException.ThrowIfNull(options);

        _inference = inference;
        _options = options.Normalized();

        if (_options.Whitelist.Count > 0)
        {
            var known = new HashSet<string>(_inference.ClassNames);
            foreach (var name in _options.Whitelist)
            {
                if (!known.Contains(name))
                    throw new DetectionException(
                        $"白名单类名 {name} 不在模型类别表 [{string.Join(", ", _inference.ClassNames)}] 中——换刷点＝换权重+类别");
            }

            _whitelist = new HashSet<string>(_options.Whitelist);
        }

        _inputBuffer = new float[3 * inference.InputHeight * inference.InputWidth];
        _outputBuffer = new float[inference.OutputFloatCount];
    }

    public string BackendName => DetectionBackends.OnnxYolo;

    public IReadOnlyList<string> ClassNames => _inference.ClassNames;

    public DetectionOptions AppliedOptions => _options;

    public IReadOnlyList<DetectedBox> Detect(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var geometry = YoloPreprocessing.LetterboxToTensor(
            bgraPixels, width, height, _inference.InputWidth, _inference.InputHeight, _inputBuffer);
        _inference.Run(_inputBuffer, _outputBuffer);
        return YoloPostprocessing.ExtractDetections(
            _outputBuffer, _inference.ClassNames,
            _options.ConfidenceThreshold, _options.IouThreshold, _options.MaxBoxes,
            geometry, width, height, _whitelist);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _inference.Dispose();
    }
}
