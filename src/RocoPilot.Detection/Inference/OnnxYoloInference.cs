using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace RocoPilot.Detection.Inference;

internal sealed class OnnxYoloInference : IYoloInference
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly long[] _inputShape;
    private int _disposed;

    internal OnnxYoloInference(string modelPath, bool useGpu = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
            throw new DetectionException($"模型文件不在：{modelPath}（构建期资产应随输出目录流转，票 04）");

        try
        {
            var sessionOptions = new SessionOptions();
            if (useGpu)
            {
                sessionOptions.AppendExecutionProvider_DML(0);
            }

            sessionOptions.AppendExecutionProvider_CPU();
            _session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new DetectionException($"ONNX 会话装载失败（{modelPath}）：{ex.Message}", ex);
        }

        try
        {
            _inputName = _session.InputNames[0];
            var inputDims = _session.InputMetadata[_inputName].Dimensions;
            if (inputDims.Length != 4 || inputDims[0] != 1 || inputDims[1] != 3 || inputDims[2] <= 0 || inputDims[3] <= 0)
                throw new DetectionException(
                    $"模型输入须为静态 [1,3,H,W]，实得 [{string.Join(',', inputDims)}]——票 04 导出契约不符");
            InputHeight = inputDims[2];
            InputWidth = inputDims[3];
            _inputShape = [1, 3, InputHeight, InputWidth];

            if (!_session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out var namesRaw)
                || string.IsNullOrWhiteSpace(namesRaw))
                throw new DetectionException("模型元数据缺 names 键——须为 ultralytics 导出（票 04 export_onnx.py）");
            ClassNames = YoloClassNames.Parse(namesRaw);

            _outputName = _session.OutputNames[0];
            var outputDims = _session.OutputMetadata[_outputName].Dimensions;
            if (outputDims.Length != 3 || outputDims[0] != 1 || outputDims[1] != 4 + ClassNames.Count || outputDims[2] <= 0)
                throw new DetectionException(
                    $"模型输出须为静态 [1,{4 + ClassNames.Count},na]，实得 [{string.Join(',', outputDims)}]——票 04 导出契约不符");
            OutputFloatCount = outputDims[0] * outputDims[1] * outputDims[2];
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    public int InputHeight { get; }

    public int InputWidth { get; }

    public IReadOnlyList<string> ClassNames { get; }

    public int OutputFloatCount { get; }

    public void Run(ReadOnlyMemory<float> input, Memory<float> output)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var inputCount = 3 * InputHeight * InputWidth;
        if (input.Length != inputCount)
            throw new ArgumentException($"输入须恰含 {inputCount} 个 float（1×3×{InputHeight}×{InputWidth}），实得 {input.Length}", nameof(input));
        if (output.Length != OutputFloatCount)
            throw new ArgumentException($"输出须恰长 {OutputFloatCount}，实得 {output.Length}", nameof(output));

        using var inputTensor = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance, MemoryMarshal.AsMemory(input), _inputShape);
        var inputs = new Dictionary<string, OrtValue>(1) { [_inputName] = inputTensor };
        using var runOptions = new RunOptions();
        using var results = _session.Run(runOptions, inputs, [_outputName]);
        results[0].GetTensorDataAsSpan<float>().CopyTo(output.Span);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _session.Dispose();
    }
}
