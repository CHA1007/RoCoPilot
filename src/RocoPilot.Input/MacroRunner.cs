namespace RocoPilot.Input;

public static class MacroRunner
{
    public static void Run(IInputDriver driver, IReadOnlyList<MacroStep> steps, Action<int>? sleepMs = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(steps);
        var sleep = sleepMs ?? Thread.Sleep;

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step.Kind)
            {
                case MacroStepKind.Press:
                    driver.KeyDown(step.Key);
                    sleep(step.Milliseconds);
                    driver.KeyUp(step.Key);
                    break;
                case MacroStepKind.Hold:
                    driver.KeyDown(step.Key);
                    sleep(step.Milliseconds);
                    break;
                case MacroStepKind.Release:
                    driver.KeyUp(step.Key);
                    break;
                case MacroStepKind.Wait:
                    SleepWithCancellation(step.Milliseconds, sleep, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(steps), $"未知宏步骤 {step.Kind}");
            }
        }
    }

    private static void SleepWithCancellation(int milliseconds, Action<int> sleep, CancellationToken cancellationToken)
    {
        const int Segment = 50;
        var remaining = milliseconds;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Math.Min(Segment, remaining);
            sleep(chunk);
            remaining -= chunk;
        }
    }
}
