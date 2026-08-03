namespace RocoPilot.Loop;

public sealed record ArmingStep(string Name, string Hint, Func<CancellationToken, Task> Execute)
{
    public Func<Exception, string>? Remedy { get; init; }

    public bool Quiet { get; init; }
}
