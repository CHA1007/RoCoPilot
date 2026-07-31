namespace RocoPilot.Input;

public static class MacroRunner
{
    public static void Run(IInputDriver driver, IReadOnlyList<MacroStep> steps, Action<int>? sleepMs = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(steps);
        var sleep = sleepMs ?? Thread.Sleep;

        foreach (var step in steps)
        {
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
                    sleep(step.Milliseconds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(steps), $"未知宏步骤 {step.Kind}");
            }
        }
    }
}
