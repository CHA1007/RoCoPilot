namespace RocoPilot.Core;

public interface IRunningTask
{
    string ToolId { get; }

    TaskState State { get; }

    event EventHandler<TaskState>? StateChanged;

    event EventHandler<ToolEvent>? EventRaised;

    object? DiagnosticsContext => null;

    void Start();

    void RequestPause(string source = "manual");

    void RequestResume(string source = "manual");

    void RequestStop();

    Task WhenStopped { get; }
}
