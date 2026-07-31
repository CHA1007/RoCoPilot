namespace RocoPilot.Detection.Inference;

internal interface IYoloInference : IDisposable
{
    int InputHeight { get; }

    int InputWidth { get; }

    IReadOnlyList<string> ClassNames { get; }

    int OutputFloatCount { get; }

    void Run(ReadOnlyMemory<float> input, Memory<float> output);
}
