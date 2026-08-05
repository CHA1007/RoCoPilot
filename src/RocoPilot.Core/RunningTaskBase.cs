namespace RocoPilot.Core;

public abstract class RunningTaskBase : IRunningTask, IDisposable
{
    private readonly TaskCompletionSource _idleWhenStopped = new();

    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _stoppedTcs;

    protected RunningTaskBase() => _idleWhenStopped.SetResult();

    protected object Gate { get; } = new();

    protected TaskState CurrentState { get; set; } = TaskState.Idle;

    public abstract string ToolId { get; }

    public virtual object? DiagnosticsContext => null;

    public TaskState State
    {
        get { lock (Gate) { return CurrentState; } }
    }

    public Task WhenStopped
    {
        get { lock (Gate) { return _stoppedTcs?.Task ?? _idleWhenStopped.Task; } }
    }

    public event EventHandler<TaskState>? StateChanged;

    public event EventHandler<ToolEvent>? EventRaised;

    public void Start()
    {
        CancellationToken token;
        lock (Gate)
        {
            if (CurrentState != TaskState.Idle)
            {
                throw new InvalidOperationException($"单活跃任务：当前态 {CurrentState}，仅 Idle 可启动");
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token = _cts.Token;
            CurrentState = TaskState.Arming;
        }

        RaiseStateChanged(TaskState.Arming);
        _ = Task.Run(() => RunWorkerAsync(token));
    }

    public abstract void RequestPause(string source = "manual");

    public abstract void RequestResume(string source = "manual");

    public void RequestStop()
    {
        lock (Gate)
        {
            if (CurrentState is not (TaskState.Arming or TaskState.Running or TaskState.Paused))
            {
                return;
            }

            CurrentState = TaskState.Stopping;
            _cts?.Cancel();
        }

        RaiseStateChanged(TaskState.Stopping);
    }

    public void Dispose()
    {
        lock (Gate)
        {
            _cts?.Cancel();
        }

        try
        {
            WhenStopped.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }

        lock (Gate)
        {
            _cts?.Dispose();
            _cts = null;
        }

        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
    }

    protected abstract Task RunWorkerAsync(CancellationToken cancellationToken);

    protected void CancelWorker()
    {
        lock (Gate)
        {
            _cts?.Cancel();
        }
    }

    protected bool TryEnterRunning()
    {
        lock (Gate)
        {
            if (CurrentState != TaskState.Arming)
            {
                return false;
            }

            CurrentState = TaskState.Running;
            return true;
        }
    }

    protected void FinishStopped()
    {
        lock (Gate)
        {
            CurrentState = TaskState.Idle;
        }

        RaiseStateChanged(TaskState.Idle);
        _stoppedTcs?.TrySetResult();
    }

    protected void RaiseStateChanged(TaskState state) => StateChanged?.Invoke(this, state);

    protected void RaiseEvent(ToolEvent toolEvent) => EventRaised?.Invoke(this, toolEvent);

    protected void SafeRaiseEvent(ToolEvent toolEvent)
    {
        try
        {
            RaiseEvent(toolEvent);
        }
        catch
        {
        }
    }
}
