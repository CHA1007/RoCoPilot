using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Settings;
using RocoPilot.Tools.AutoBattle;
using RocoPilot.Tools.AutoBattle.Battle;
using RocoPilot.Tools.AutoThrow;
using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Shell.Services;

public sealed class DispatcherHost : IDisposable
{
    private readonly CaptureHost _capture;
    private readonly ISettingsStore _store;
    private readonly AutoThrowTool _throwTool;

    private readonly object _gate = new();
    private SceneDispatcherRunningTask? _task;
    private AutoThrowHandler? _throwHandler;
    private AutoBattleHandler? _battleHandler;
    private FastTravelHandler? _fastTravelHandler;
    private bool _autoThrowEnabled;
    private bool _autoBattleEnabled;
    private bool _fastTravelEnabled;

    public DispatcherHost(CaptureHost capture, ISettingsStore store, AutoThrowTool throwTool)
    {
        _capture = capture;
        _store = store;
        _throwTool = throwTool;

        var shell = store.GetShellSettings();
        _autoThrowEnabled = shell.AutoThrowEnabled;
        _autoBattleEnabled = shell.AutoBattleEnabled;
        _fastTravelEnabled = shell.FastTravelEnabled;

        _capture.Changed += OnCaptureChanged;
    }

    public GameScene CurrentScene { get; private set; } = GameScene.Unknown;

    public object? DiagnosticsContext
    {
        get
        {
            lock (_gate) { return _throwHandler?.Pipeline; }
        }
    }

    public bool IsRunning
    {
        get { lock (_gate) { return _task is not null; } }
    }

    public TaskState DispatcherState
    {
        get { lock (_gate) { return _task?.State ?? TaskState.Idle; } }
    }

    public event EventHandler<TaskState>? TaskStateChanged;

    public bool AutoThrowEnabled
    {
        get => _autoThrowEnabled;
        set
        {
            if (_autoThrowEnabled == value) return;
            _autoThrowEnabled = value;
            PersistEnables();
        }
    }

    public bool AutoBattleEnabled
    {
        get => _autoBattleEnabled;
        set
        {
            if (_autoBattleEnabled == value) return;
            _autoBattleEnabled = value;
            PersistEnables();
        }
    }

    public bool FastTravelEnabled
    {
        get => _fastTravelEnabled;
        set
        {
            if (_fastTravelEnabled == value) return;
            _fastTravelEnabled = value;
            PersistEnables();
        }
    }

    private void PersistEnables()
    {
        var shell = _store.GetShellSettings();
        shell.AutoThrowEnabled = _autoThrowEnabled;
        shell.AutoBattleEnabled = _autoBattleEnabled;
        shell.FastTravelEnabled = _fastTravelEnabled;
        _store.SetShellSettings(shell);
        _store.Save();
    }

    public event Action? Changed;

    public event EventHandler<ToolEvent>? EventRaised;

    private void OnCaptureChanged()
    {
        if (_capture.IsRunning)
            TryStart();
        else
            Stop();
    }

    private void TryStart()
    {
        lock (_gate)
        {
            if (_task is not null) return;

            var source = _capture.CurrentSource;
            if (source is null)
            {
                Trace.TraceWarning("[DispatcherHost] 截图源为 null，跳过启动");
                return;
            }

            Trace.TraceInformation("[DispatcherHost] 截图器已启动，拉起调度器");

            var throwSettings = _store.GetToolSettings(
                _throwTool.Id, _throwTool.SettingsType, _throwTool.CreateDefaultSettings) as AutoThrowSettings
                                ?? new AutoThrowSettings();
            var battleSettings = _store.GetToolSettings(
                "auto-battle", typeof(AutoBattleSettings), () => new AutoBattleSettings()) as AutoBattleSettings
                                 ?? new AutoBattleSettings();

            _throwHandler = new AutoThrowHandler(throwSettings, source, _store)
            {
                IsEnabled = AutoThrowEnabled,
            };

            var sensor = new TemplateBattleSensor("assets/templates/panel", "assets/templates/skills");
            _battleHandler = new AutoBattleHandler(battleSettings, sensor)
            {
                IsEnabled = AutoBattleEnabled,
            };

            var fastTravelSettings = _store.GetToolSettings(
                "fast-travel", typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
                                     ?? new FastTravelSettings();
            _fastTravelHandler = new FastTravelHandler(
                fastTravelSettings,
                TeleportSensor.TryCreate("assets/templates/map/teleport.png"),
                GameFrameMapper.Create(source))
            {
                IsEnabled = FastTravelEnabled,
            };

            var shell = _store.GetShellSettings();
            var task = new SceneDispatcherRunningTask(
                captureSourceProvider: () => _capture.CurrentSource,
                driverFactory: () => InputDriverFactory.Create("interception"),
                isGameForeground: () => WindowFinder.IsForegroundProcess(WindowFinder.GameProcessName),
                detectorFactory: () => SceneDetectors.CreateAll(),
                handlerFactory: () => new Dictionary<GameScene, ISceneHandler>
                {
                    [GameScene.OpenWorld] = _throwHandler,
                    [GameScene.Battle] = _battleHandler,
                    [GameScene.WorldMap] = _fastTravelHandler,
                });

            task.EventRaised += OnTaskEvent;
            task.StateChanged += OnTaskStateChanged;
            _task = task;
            task.Start();
        }

        Changed?.Invoke();
    }

    private void Stop()
    {
        Trace.TraceInformation("[DispatcherHost] 截图器停止，收起调度器");
        SceneDispatcherRunningTask? task;
        lock (_gate)
        {
            task = _task;
            _task = null;
            _throwHandler = null;
            _battleHandler = null;
            _fastTravelHandler = null;
        }

        if (task is null) return;

        task.EventRaised -= OnTaskEvent;
        task.StateChanged -= OnTaskStateChanged;
        task.RequestStop();

        try { task.WhenStopped.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }

        task.Dispose();
        CurrentScene = GameScene.Unknown;
        Changed?.Invoke();
    }

    public void SyncEnables()
    {
        SceneDispatcherRunningTask? task;
        lock (_gate)
        {
            if (_throwHandler is not null) _throwHandler.IsEnabled = AutoThrowEnabled;
            if (_battleHandler is not null) _battleHandler.IsEnabled = AutoBattleEnabled;
            if (_fastTravelHandler is not null) _fastTravelHandler.IsEnabled = FastTravelEnabled;
            task = _task;
        }

        task?.RequestRefreshActivation();
    }

    private void OnTaskEvent(object? sender, ToolEvent e)
    {
        if (e.Name == "scene_changed" && e.Data?["to"] is string to)
        {
            CurrentScene = Enum.TryParse<GameScene>(to, out var scene) ? scene : GameScene.Unknown;
            Changed?.Invoke();
        }

        EventRaised?.Invoke(this, e);
    }

    private void OnTaskStateChanged(object? sender, TaskState state)
    {
        if (state == TaskState.Idle)
        {
            lock (_gate) { _task = null; }
            Changed?.Invoke();
        }

        TaskStateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _capture.Changed -= OnCaptureChanged;
        Stop();
    }
}
