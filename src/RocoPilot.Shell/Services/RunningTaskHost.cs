using RocoPilot.Core;

namespace RocoPilot.Shell.Services;

public sealed record ActiveLaunch(ITool Tool, object Settings);

public sealed class RunningTaskHost
{
    private readonly object _gate = new();
    private IRunningTask? _current;
    private ActiveLaunch? _active;

    public IRunningTask? Current
    {
        get { lock (_gate) { return _current; } }
    }

    public ActiveLaunch? Active
    {
        get { lock (_gate) { return _active; } }
    }

    public event Action? Changed;

    public bool TryStart(ITool tool, object settings)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var task = tool.Run(settings);
        lock (_gate)
        {
            if (_current is not null)
            {
                (task as IDisposable)?.Dispose();
                return false;
            }

            _current = task;
            _active = new ActiveLaunch(tool, settings);
        }

        task.Start();
        _ = task.WhenStopped.ContinueWith(_ => OnTaskStopped(task), TaskScheduler.Default);
        Changed?.Invoke();
        return true;
    }

    public void RequestPause(string source = "shell") => Current?.RequestPause(source);

    public void RequestResume(string source = "shell") => Current?.RequestResume(source);

    public void RequestStop() => Current?.RequestStop();

    public void TogglePauseResume(string source = "f12")
    {
        var current = Current;
        switch (current?.State)
        {
            case TaskState.Running:
                current.RequestPause(source);
                break;
            case TaskState.Paused:
                current.RequestResume(source);
                break;
        }
    }

    private void OnTaskStopped(IRunningTask task)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, task))
            {
                _current = null;
                _active = null;
            }
        }

        (task as IDisposable)?.Dispose();
        Changed?.Invoke();
    }
}
