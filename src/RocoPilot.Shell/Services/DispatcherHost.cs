using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Routing;
using RocoPilot.Scripting;
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
    private readonly ITool _throwTool;
    private readonly RouteStore _routeStore;
    private readonly ScriptStore _scriptStore;

    private readonly object _gate = new();
    private IDisposable? _captureLease;
    private SceneDispatcherRunningTask? _task;
    private AutoThrowHandler? _throwHandler;
    private RouteExecutionHandler? _routeHandler;
    private OpenWorldModeSelector? _openWorld;
    private AutoBattleHandler? _battleHandler;
    private FastTravelHandler? _fastTravelHandler;
    private FastTravelSettings? _fastTravelSettings;
    private FastTravelTriggerMode _fastTravelTriggerMode;
    private OpenWorldModule _openWorldModule;
    private bool _autoBattleEnabled;
    private bool _fastTravelEnabled;
    private Guid? _pendingStartNode;
    private bool _pendingSingleNode;

    public DispatcherHost(CaptureHost capture, ISettingsStore store, ITool throwTool, RouteStore routeStore, ScriptStore scriptStore)
    {
        _capture = capture;
        _store = store;
        _throwTool = throwTool;
        _routeStore = routeStore;
        _scriptStore = scriptStore;

        var shell = store.GetShellSettings();
        _openWorldModule = shell.AutoThrowEnabled ? OpenWorldModule.AutoThrow : OpenWorldModule.None;
        _autoBattleEnabled = shell.AutoBattleEnabled;
        _fastTravelEnabled = shell.FastTravelEnabled;
        _fastTravelTriggerMode = (_store.GetToolSettings(
                FastTravelTool.Id, typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings)
            ?.TriggerMode ?? FastTravelTriggerMode.Auto;

        _capture.Changed += OnCaptureChanged;
        UpdateCaptureLease();
    }

    public GameScene CurrentScene { get; private set; } = GameScene.Unknown;

    public IReadOnlyDictionary<GameScene, float> SceneScores
    {
        get { lock (_gate) { return _task?.SceneScores ?? new Dictionary<GameScene, float>(); } }
    }

    public void StartRouteExecution(Guid? startNodeId, bool singleNode)
    {
        lock (_gate)
        {
            _pendingStartNode = startNodeId;
            _pendingSingleNode = singleNode;
            if (_routeHandler is not null)
            {
                _routeHandler.StartNodeId = startNodeId;
                _routeHandler.SingleNode = singleNode;
            }
        }

        RouteExecutionEnabled = true;
        UpdateCaptureLease();
        SyncEnables();
    }

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
        get => _openWorldModule == OpenWorldModule.AutoThrow;
        set
        {
            var next = value ? OpenWorldModule.AutoThrow : OpenWorldModule.None;
            if (_openWorldModule == next) return;
            _openWorldModule = next;
            PersistEnables();
            UpdateCaptureLease();
        }
    }

    public bool RouteExecutionEnabled
    {
        get => _openWorldModule == OpenWorldModule.Route;
        set
        {
            var next = value ? OpenWorldModule.Route : OpenWorldModule.None;
            if (_openWorldModule == next) return;
            _openWorldModule = next;
            PersistEnables();
            UpdateCaptureLease();
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
            UpdateCaptureLease();
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
            UpdateCaptureLease();
        }
    }

    private void UpdateCaptureLease()
    {
        var armed = AutoThrowEnabled || AutoBattleEnabled || FastTravelEnabled || RouteExecutionEnabled;
        if (armed && _captureLease is null)
        {
            _captureLease = _capture.Acquire(CaptureHost.DispatcherConsumer);
        }
        else if (!armed && _captureLease is not null)
        {
            _captureLease.Dispose();
            _captureLease = null;
        }
    }

    public FastTravelTriggerMode FastTravelTriggerMode
    {
        get => _fastTravelTriggerMode;
        set
        {
            if (_fastTravelTriggerMode == value) return;
            _fastTravelTriggerMode = value;

            var settings = _store.GetToolSettings(
                FastTravelTool.Id, typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
                           ?? new FastTravelSettings();
            settings.TriggerMode = value;
            _store.SetToolSettings(FastTravelTool.Id, settings);
            _store.Save();

            lock (_gate)
            {
                if (_fastTravelSettings is not null) _fastTravelSettings.TriggerMode = value;
            }
        }
    }

    public void TriggerFastTravel()
    {
        lock (_gate)
        {
            _fastTravelHandler?.RequestTeleport();
        }
    }

    private bool EffectiveFastTravelEnabled =>
        _fastTravelEnabled && _openWorldModule != OpenWorldModule.Route;

    private void PersistEnables()
    {
        var shell = _store.GetShellSettings();
        shell.AutoThrowEnabled = _openWorldModule == OpenWorldModule.AutoThrow;
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
                AutoBattleTool.Id, typeof(AutoBattleSettings), () => new AutoBattleSettings()) as AutoBattleSettings
                                 ?? new AutoBattleSettings();

            _throwHandler = new AutoThrowHandler(throwSettings, source, _store)
            {
                IsEnabled = AutoThrowEnabled,
            };

            _routeHandler = new RouteExecutionHandler(
                _routeStore,
                _scriptStore,
                _store,
                () => _capture.CurrentSource)
            {
                IsEnabled = RouteExecutionEnabled,
                StartNodeId = _pendingStartNode,
                SingleNode = _pendingSingleNode,
            };

            _openWorld = new OpenWorldModeSelector(_routeHandler, _throwHandler);

            var sensor = new TemplateBattleSensor("assets/templates/panel", "assets/templates/skills");
            _battleHandler = new AutoBattleHandler(battleSettings, sensor)
            {
                IsEnabled = AutoBattleEnabled,
            };

            var fastTravelSettings = _store.GetToolSettings(
                FastTravelTool.Id, typeof(FastTravelSettings), () => new FastTravelSettings()) as FastTravelSettings
                                     ?? new FastTravelSettings();
            fastTravelSettings.TriggerMode = _fastTravelTriggerMode;
            _fastTravelSettings = fastTravelSettings;
            _fastTravelHandler = new FastTravelHandler(
                fastTravelSettings,
                TeleportSensor.TryCreate("assets/templates/map/teleport.png"),
                GameFrameMapper.Create(source))
            {
                IsEnabled = EffectiveFastTravelEnabled,
            };

            var shell = _store.GetShellSettings();
            var task = new SceneDispatcherRunningTask(
                captureSourceProvider: () => _capture.CurrentSource,
                driverFactory: InputDriverFactory.Create,
                isGameForeground: () => WindowFinder.IsForegroundProcess(WindowFinder.GameProcessName),
                detectorFactory: () => SceneDetectors.CreateAll(),
                handlerFactory: () => new Dictionary<GameScene, ISceneHandler>
                {
                    [GameScene.OpenWorld] = _openWorld,
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
        RouteExecutionHandler? route;
        lock (_gate)
        {
            task = _task;
            _task = null;
            _openWorld = null;
            _throwHandler = null;
            route = _routeHandler;
            _routeHandler = null;
            _battleHandler = null;
            _fastTravelHandler = null;
            _fastTravelSettings = null;
        }

        if (task is null)
        {
            route?.Dispose();
            return;
        }

        task.EventRaised -= OnTaskEvent;
        task.StateChanged -= OnTaskStateChanged;
        task.RequestStop();

        try { task.WhenStopped.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }

        task.Dispose();
        route?.Dispose();
        CurrentScene = GameScene.Unknown;
        Changed?.Invoke();
    }

    public void SyncEnables()
    {
        SceneDispatcherRunningTask? task;
        lock (_gate)
        {
            if (_throwHandler is not null) _throwHandler.IsEnabled = AutoThrowEnabled;
            if (_routeHandler is not null) _routeHandler.IsEnabled = RouteExecutionEnabled;
            if (_battleHandler is not null) _battleHandler.IsEnabled = AutoBattleEnabled;
            if (_fastTravelHandler is not null) _fastTravelHandler.IsEnabled = EffectiveFastTravelEnabled;
            _openWorld?.ApplySelection();
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
        _captureLease?.Dispose();
        _captureLease = null;
        Stop();
    }

    private sealed class OpenWorldModeSelector : ISceneHandler
    {
        private readonly ISceneHandler[] _modesByPriority;
        private ISceneHandler? _active;
        private SceneContext? _context;

        public OpenWorldModeSelector(params ISceneHandler[] modesByPriority)
            => _modesByPriority = modesByPriority;

        public GameScene Scene => GameScene.OpenWorld;

        public bool IsEnabled => _modesByPriority.Any(mode => mode.IsEnabled);

        private ISceneHandler? SelectedMode => _modesByPriority.FirstOrDefault(mode => mode.IsEnabled);

        public void Activate(SceneContext context)
        {
            _context = context;
            if (SelectedMode is not { } mode) return;
            mode.Activate(context);
            _active = mode;
        }

        public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
        {
            var active = _active;
            if (active is null || !ReferenceEquals(active, SelectedMode)) return false;
            return active.Handle(bgraPixels, width, height);
        }

        public void Deactivate()
        {
            _active?.Deactivate();
            _active = null;
            _context = null;
        }

        public void SuspendSensing() => _active?.SuspendSensing();

        public void ResumeSensing() => _active?.ResumeSensing();

        public bool HoldActivation(GameScene nextScene) => _active?.HoldActivation(nextScene) ?? false;

        public bool PauseOnFocusLost() => _active?.PauseOnFocusLost() ?? false;

        public void ResumeAfterFocusRestored() => _active?.ResumeAfterFocusRestored();

        public void ApplySelection()
        {
            var selected = SelectedMode;
            if (ReferenceEquals(_active, selected)) return;

            _active?.Deactivate();
            _active = null;

            if (selected is not null && _context is not null && selected.IsEnabled)
            {
                selected.Activate(_context);
                _active = selected;
            }
        }
    }

    private enum OpenWorldModule
    {
        None,
        AutoThrow,
        Route,
    }
}
