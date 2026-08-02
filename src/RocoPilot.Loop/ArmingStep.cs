namespace RocoPilot.Loop;

public sealed record ArmingStep(string Name, string Hint, Func<CancellationToken, Task> Execute)
{
    public Func<Exception, string>? Remedy { get; init; }

    // 瞬时且必跑的步骤不刷进度提示，避免误导用户以为是可选自检；失败仍照常上报。
    public bool Quiet { get; init; }
}
