using System.IO;
using System.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Loop;
using RocoPilot.Settings;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowHandler : ISceneHandler
{
    private readonly AutoThrowSettings _settings;
    private readonly ICaptureSource _captureSource;
    private readonly ISettingsStore _store;
    private readonly Func<AutoThrowSettings>? _settingsProvider;

    private static readonly TimeSpan OptionRefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Thread? _optionRefresher;
    private ICatchPipeline? _pipeline;
    private SceneContext? _context;

    public AutoThrowHandler(
        AutoThrowSettings settings,
        ICaptureSource captureSource,
        ISettingsStore store,
        Func<AutoThrowSettings>? settingsProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settingsProvider = settingsProvider;
    }

    public GameScene Scene => GameScene.OpenWorld;

    public bool IsEnabled { get; set; } = true;

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

        return _runTask is not null && !_runTask.IsCompleted;
    }

    public void SuspendSensing()
    {
        ICatchPipeline? pipeline;
        lock (_gate) { pipeline = _pipeline; }

        if (pipeline is null)
            return;

        if (pipeline.Pause("scene_doubt"))
        {
            pipeline.SetSensing(false);
        }
    }

    public void ResumeSensing()
    {
        ICatchPipeline? pipeline;
        lock (_gate) { pipeline = _pipeline; }

        if (pipeline is null)
            return;

        if (!pipeline.Resume("scene_doubt"))
            return;

        if (pipeline.InputGate())
        {
            pipeline.SetSensing(true);
        }
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

            if (!await Arming.ExecuteAsync(pipeline.ArmingSteps, e => _context?.EmitEvent(e), ct))
            {
                return;
            }

            _context?.EmitEvent(new ToolEvent("auto_throw_started"));
            StartOptionRefresher(pipeline, ct);

            pipeline.Bus.EventRaised += OnPipelineEvent;
            try
            {
                await Task.Run(() => pipeline.Run(ct), ct);
            }
            finally
            {
                pipeline.Bus.EventRaised -= OnPipelineEvent;
            }
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
            StopOptionRefresher();
            lock (_gate)
            {
                if (ReferenceEquals(_pipeline, pipeline))
                    _pipeline = null;
            }

            pipeline?.Dispose();
            _context?.EmitEvent(new ToolEvent("auto_throw_stopped"));
        }
    }

    private void StartOptionRefresher(ICatchPipeline pipeline, CancellationToken ct)
    {
        StopOptionRefresher();
        _optionRefresher = new Thread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                ct.WaitHandle.WaitOne(OptionRefreshInterval);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                ApplyLatestOptions(pipeline);
            }
        })
        {
            IsBackground = true,
            Name = "自动丢球配置热刷新",
        };
        _optionRefresher.Start();
    }

    private void StopOptionRefresher()
    {
        var refresher = _optionRefresher;
        _optionRefresher = null;
        if (refresher is not null && refresher.IsAlive)
        {
            refresher.Join(TimeSpan.FromSeconds(1));
        }
    }

    private void ApplyLatestOptions(ICatchPipeline pipeline) =>
        pipeline.ApplyLoopOptions(((_settingsProvider?.Invoke()) ?? _settings).ToLoopOptions());

    private void OnPipelineEvent(object? sender, ToolEvent e) => _context?.EmitEvent(e);

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
