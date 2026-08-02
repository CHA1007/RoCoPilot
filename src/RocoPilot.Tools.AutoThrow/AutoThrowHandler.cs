using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Loop;
using RocoPilot.Settings;

namespace RocoPilot.Tools.AutoThrow;

/// <summary>
/// 自动丢球的场景处理器：包装 <see cref="CatchPipeline"/>，
/// 由 <see cref="SceneDispatcher"/> 在大世界场景切入/切出时管理生命周期。
/// 管线自持帧循环（StreamingTargetSensor 订阅 FrameArrived），Handle 无需喂帧。
/// </summary>
public sealed class AutoThrowHandler : ISceneHandler
{
    private readonly AutoThrowSettings _settings;
    private readonly ICaptureSource _captureSource;
    private readonly ISettingsStore _store;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private ICatchPipeline? _pipeline;
    private SceneContext? _context;

    public AutoThrowHandler(AutoThrowSettings settings, ICaptureSource captureSource, ISettingsStore store)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public GameScene Scene => GameScene.OpenWorld;

    public bool IsEnabled { get; set; } = true;

    /// <summary>调试叠层用。</summary>
    public ICatchPipeline? Pipeline
    {
        get { lock (_gate) { return _pipeline; } }
    }

    public void Activate(SceneContext context)
    {
        _context = context;

        lock (_gate)
        {
            if (_runTask is not null) return;
            _cts = new CancellationTokenSource();
        }

        _runTask = Task.Run(() => RunPipelineAsync(_cts.Token));
    }

    public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        // 管线自持帧循环，无需外部喂帧
        return _runTask is not null && !_runTask.IsCompleted;
    }

    public void Deactivate()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_gate)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
        }

        cts?.Cancel();

        try { runTask?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }

        lock (_gate)
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }

        cts?.Dispose();
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        ICatchPipeline? pipeline = null;
        try
        {
            pipeline = CreatePipeline();
            lock (_gate) { _pipeline = pipeline; }

            // Arming
            foreach (var step in pipeline.ArmingSteps)
            {
                if (!step.Quiet)
                {
                    _context?.EmitEvent(new ToolEvent("arming_step", new Dictionary<string, object?>
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
                    _context?.EmitEvent(new ToolEvent("arming_failed", new Dictionary<string, object?>
                    {
                        ["step"] = step.Name,
                        ["error"] = cause.Message,
                        ["remedy"] = step.Remedy?.Invoke(cause) ?? "查日志排障后重试",
                    }));
                    return;
                }
            }

            // 运行
            _context?.EmitEvent(new ToolEvent("auto_throw_started"));
            await Task.Run(() => pipeline.Run(ct), ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _context?.EmitEvent(new ToolEvent("fault", new Dictionary<string, object?>
            {
                ["error"] = ex.GetBaseException().Message,
                ["source"] = "auto_throw",
            }));
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pipeline, pipeline))
                    _pipeline = null;
            }

            pipeline?.Dispose();
            _context?.EmitEvent(new ToolEvent("auto_throw_stopped"));
        }
    }

    private ICatchPipeline CreatePipeline()
    {
        LogRetention.PruneSessions(RocoPaths.LogsRoot);
        var sessionDir = Path.Combine(RocoPaths.LogsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var shell = _store.GetShellSettings();
        var spec = _settings.ToPipelineSpec() with
        {
            SessionLogDirectory = sessionDir,
            ExistingSource = _captureSource,
        };

        var centering = spec.Centering;
        if (shell.TurnFallbackDivisor > 0)
            centering = centering with { FallbackDivisor = shell.TurnFallbackDivisor };

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
                var current = _store.GetShellSettings();
                current.SensitivityPpcX = Math.Round(ppcX, 3);
                current.SensitivityPpcY = Math.Round(ppcY, 3);
                _store.SetShellSettings(current);
                _store.Save();
            },
        });
    }
}
