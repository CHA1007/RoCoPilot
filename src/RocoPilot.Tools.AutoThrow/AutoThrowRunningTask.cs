using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Loop;
using RocoPilot.Settings;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowRunningTask : IRunningTask, IDisposable
{
    private readonly Func<ICatchPipeline> _pipelineFactory;
    private readonly TimeSpan _focusPollInterval;

    private readonly object _gate = new();
    private readonly TaskCompletionSource _idleWhenStopped = new();

    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _stoppedTcs;
    private TaskState _state = TaskState.Idle;
    private ICatchPipeline? _pipeline;
    private Thread? _focusWatcher;

    public AutoThrowRunningTask(AutoThrowSettings settings, ICaptureSource captureSource, ISettingsStore store)
        : this(() => CreatePipeline(settings ?? throw new ArgumentNullException(nameof(settings)), captureSource, store))
    {
    }

    internal AutoThrowRunningTask(Func<ICatchPipeline> pipelineFactory, TimeSpan? focusPollInterval = null)
    {
        _pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        _focusPollInterval = focusPollInterval ?? TimeSpan.FromMilliseconds(250);
        _idleWhenStopped.SetResult();
    }

    public string ToolId => AutoThrowTool.ToolId;

    public TaskState State
    {
        get { lock (_gate) { return _state; } }
    }

    public Task WhenStopped
    {
        get { lock (_gate) { return _stoppedTcs?.Task ?? _idleWhenStopped.Task; } }
    }

    /// <summary>调试叠层用：Arming 完成后非空，停止后置空。</summary>
    public ICatchPipeline? Pipeline
    {
        get { lock (_gate) { return _pipeline; } }
    }

    public object? DiagnosticsContext => Pipeline;

    public event EventHandler<TaskState>? StateChanged;

    public event EventHandler<ToolEvent>? EventRaised;

    public void Start()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_state != TaskState.Idle)
            {
                throw new InvalidOperationException($"单活跃任务：当前态 {_state}，仅 Idle 可启动（ADR-0003）");
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token = _cts.Token;
            _state = TaskState.Arming;
        }

        RaiseStateChanged(TaskState.Arming);
        _ = Task.Run(() => RunWorkerAsync(token));
    }

    public void RequestPause(string source = "manual")
    {
        ICatchPipeline? pipeline;
        lock (_gate)
        {
            if (_state != TaskState.Running)
            {
                return;
            }

            _state = TaskState.Paused;
            pipeline = _pipeline;
        }

        if (pipeline is null || !pipeline.Pause(source))
        {
            lock (_gate)
            {
                if (_state == TaskState.Paused)
                {
                    _state = TaskState.Running;
                }
            }

            return;
        }

        pipeline.SetSensing(false);
        RaiseStateChanged(TaskState.Paused);
    }

    public void RequestResume(string source = "manual")
    {
        ICatchPipeline? pipeline;
        lock (_gate)
        {
            if (_state != TaskState.Paused)
            {
                return;
            }

            _state = TaskState.Running;
            pipeline = _pipeline;
        }

        if (pipeline is null || !pipeline.Resume(source))
        {
            lock (_gate)
            {
                if (_state == TaskState.Running)
                {
                    _state = TaskState.Paused;
                }
            }

            return;
        }

        if (pipeline.InputGate())
        {
            pipeline.SetSensing(true);
        }

        RaiseStateChanged(TaskState.Running);
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            if (_state is not (TaskState.Arming or TaskState.Running or TaskState.Paused))
            {
                return;
            }

            _state = TaskState.Stopping;
            _cts?.Cancel();
        }

        RaiseStateChanged(TaskState.Stopping);
    }

    public void Dispose()
    {
        lock (_gate)
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

        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static ICatchPipeline CreatePipeline(AutoThrowSettings settings, ICaptureSource captureSource, ISettingsStore store)
    {
        LogRetention.PruneSessions(RocoPaths.LogsRoot);
        var sessionDir = Path.Combine(RocoPaths.LogsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        // 视角灵敏度以全局设置（ShellSettings）为准
        var shell = store.GetShellSettings();

        var spec = settings.ToPipelineSpec() with { SessionLogDirectory = sessionDir, ExistingSource = captureSource };
        var centering = spec.Centering;

        if (shell.TurnFallbackDivisor > 0)
        {
            centering = centering with { FallbackDivisor = shell.TurnFallbackDivisor };
        }

        var loop = spec.Loop with
        {
            AimOffsetY = shell.AimOffsetY,
            PpcX = shell.SensitivityPpcX,
            PpcY = shell.SensitivityPpcY,
        };

        return new CatchPipeline(spec with
        {
            Centering = centering,
            Loop = loop,
            OnCalibrated = (ppcX, ppcY) =>
            {
                var current = store.GetShellSettings();
                current.SensitivityPpcX = Math.Round(ppcX, 3);
                current.SensitivityPpcY = Math.Round(ppcY, 3);
                store.SetShellSettings(current);
                store.Save();
            },
        });
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        ICatchPipeline? pipeline = null;
        try
        {
            pipeline = _pipelineFactory();
            lock (_gate)
            {
                _pipeline = pipeline;
            }

            foreach (var step in pipeline.ArmingSteps)
            {
                if (!step.Quiet)
                {
                    RaiseEvent(new ToolEvent("arming_step", new Dictionary<string, object?>
                    {
                        ["step"] = step.Name,
                        ["hint"] = step.Hint,
                    }));
                }

                try
                {
                    await step.Execute(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var cause = ex.GetBaseException();
                    RaiseEvent(new ToolEvent("arming_failed", new Dictionary<string, object?>
                    {
                        ["step"] = step.Name,
                        ["error"] = cause.Message,
                        ["remedy"] = step.Remedy?.Invoke(cause) ?? "查日志排障后重试",
                    }));
                    return;
                }
            }

            bool entered;
            lock (_gate)
            {
                entered = _state == TaskState.Arming;
                if (entered)
                {
                    _state = TaskState.Running;
                }
            }

            if (!entered)
            {
                return;
            }

            RaiseStateChanged(TaskState.Running);
            StartFocusWatcher(pipeline, ct);

            var bus = pipeline.Bus;
            bus.EventRaised += OnPipelineEvent;
            try
            {
                await Task.Run(() => pipeline.Run(ct));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SafeRaiseEvent(new ToolEvent("fault", new Dictionary<string, object?>
                {
                    ["error"] = ex.GetBaseException().Message,
                    ["source"] = "pipeline_run",
                }));
            }
            finally
            {
                bus.EventRaised -= OnPipelineEvent;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SafeRaiseEvent(new ToolEvent("fault", new Dictionary<string, object?>
            {
                ["error"] = ex.GetBaseException().Message,
            }));
        }
        finally
        {
            StopFocusWatcher();
            lock (_gate)
            {
                _pipeline = null;
                _state = TaskState.Idle;
            }

            pipeline?.Dispose();
            RaiseStateChanged(TaskState.Idle);
            _stoppedTcs?.TrySetResult();
        }
    }

    private void StartFocusWatcher(ICatchPipeline pipeline, CancellationToken ct)
    {
        _focusWatcher = new Thread(() => FocusWatcherLoop(pipeline, ct))
        {
            IsBackground = true,
            Name = "失焦门控",
        };
        _focusWatcher.Start();
    }

    private void FocusWatcherLoop(ICatchPipeline pipeline, CancellationToken ct)
    {
        var focused = true;
        while (!ct.IsCancellationRequested)
        {
            ct.WaitHandle.WaitOne(_focusPollInterval);
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var nowFocused = pipeline.InputGate();
            if (nowFocused == focused)
            {
                continue;
            }

            focused = nowFocused;
            if (nowFocused)
            {
                RaiseEvent(new ToolEvent("focus_regained"));
            }
            else
            {
                // 失焦只门控输入（CatchLoopEngine / CenteringController 各自持有 inputGate），
                // 识别持续运行，重新聚焦后无需等待稳定门控重新积累。
                RaiseEvent(new ToolEvent("focus_lost"));
            }
        }
    }

    private void StopFocusWatcher()
    {
        var watcher = _focusWatcher;
        if (watcher is not null && watcher.IsAlive)
        {
            watcher.Join(TimeSpan.FromSeconds(1));
        }

        _focusWatcher = null;
    }

    private void OnPipelineEvent(object? sender, ToolEvent toolEvent) => RaiseEvent(toolEvent);

    private void RaiseStateChanged(TaskState state) => StateChanged?.Invoke(this, state);

    private void RaiseEvent(ToolEvent toolEvent) => EventRaised?.Invoke(this, toolEvent);

    private void SafeRaiseEvent(ToolEvent toolEvent)
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
