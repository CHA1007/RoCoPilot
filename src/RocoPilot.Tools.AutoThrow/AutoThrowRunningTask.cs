using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Loop;
using RocoPilot.Settings;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowRunningTask : RunningTaskBase
{
    private readonly Func<ICatchPipeline> _pipelineFactory;
    private readonly TimeSpan _focusPollInterval;

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
    }

    public override string ToolId => AutoThrowTool.ToolId;

    public ICatchPipeline? Pipeline
    {
        get { lock (Gate) { return _pipeline; } }
    }

    public override object? DiagnosticsContext => Pipeline;

    public override void RequestPause(string source = "manual")
    {
        ICatchPipeline? pipeline;
        lock (Gate)
        {
            if (CurrentState != TaskState.Running)
            {
                return;
            }

            CurrentState = TaskState.Paused;
            pipeline = _pipeline;
        }

        if (pipeline is null || !pipeline.Pause(source))
        {
            lock (Gate)
            {
                if (CurrentState == TaskState.Paused)
                {
                    CurrentState = TaskState.Running;
                }
            }

            return;
        }

        pipeline.SetSensing(false);
        RaiseStateChanged(TaskState.Paused);
    }

    public override void RequestResume(string source = "manual")
    {
        ICatchPipeline? pipeline;
        lock (Gate)
        {
            if (CurrentState != TaskState.Paused)
            {
                return;
            }

            CurrentState = TaskState.Running;
            pipeline = _pipeline;
        }

        if (pipeline is null || !pipeline.Resume(source))
        {
            lock (Gate)
            {
                if (CurrentState == TaskState.Running)
                {
                    CurrentState = TaskState.Paused;
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

    private static ICatchPipeline CreatePipeline(AutoThrowSettings settings, ICaptureSource captureSource, ISettingsStore store)
    {
        LogRetention.PruneSessions(RocoPaths.LogsRoot);
        var sessionDir = Path.Combine(RocoPaths.LogsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

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

    protected override async Task RunWorkerAsync(CancellationToken ct)
    {
        ICatchPipeline? pipeline = null;
        try
        {
            pipeline = _pipelineFactory();
            lock (Gate)
            {
                _pipeline = pipeline;
            }

            if (!await Arming.ExecuteAsync(pipeline.ArmingSteps, RaiseEvent, ct))
            {
                return;
            }

            if (!TryEnterRunning())
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
            lock (Gate)
            {
                _pipeline = null;
            }

            pipeline?.Dispose();
            FinishStopped();
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
}
