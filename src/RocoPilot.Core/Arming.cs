namespace RocoPilot.Core;

public static class Arming
{
    public const string StepEvent = "arming_step";

    public const string FailedEvent = "arming_failed";

    public static async Task<bool> ExecuteAsync(
        IReadOnlyList<ArmingStep> steps,
        Action<ToolEvent> emitEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(emitEvent);

        foreach (var step in steps)
        {
            if (!step.Quiet)
            {
                emitEvent(new ToolEvent(StepEvent, new Dictionary<string, object?>
                {
                    ["step"] = step.Name,
                    ["hint"] = step.Hint,
                }));
            }

            try
            {
                await step.Execute(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var cause = ex.GetBaseException();
                emitEvent(new ToolEvent(FailedEvent, new Dictionary<string, object?>
                {
                    ["step"] = step.Name,
                    ["error"] = cause.Message,
                    ["remedy"] = step.Remedy?.Invoke(cause) ?? "查日志排障后重试",
                }));
                return false;
            }
        }

        return true;
    }
}
