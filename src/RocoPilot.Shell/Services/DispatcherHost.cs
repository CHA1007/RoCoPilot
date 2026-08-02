using System.Diagnostics;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Settings;
using RocoPilot.Tools.AutoBattle;
using RocoPilot.Tools.AutoBattle.Battle;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Services;

/// <summary>
/// 调度器宿主：截图器启动时自动拉起 <see cref="SceneDispatcher"/>，停止时自动收起。
/// 外壳级共享服务，用户无需直接操作。
/// </summary>
public sealed class DispatcherHost : IDisposable
{
    private readonly CaptureHost _capture;
    private readonly ISettingsStore _store;
    private readonly AutoThrowTool _throwTool;

    private readonly object _gate = new();
    private SceneDispatcherRunningTask? _task;
    private AutoThrowHandler? _throwHandler;
    private AutoBattleHandler? _battleHandler;
    private bool _autoThrowEnabled;
    private bool _autoBattleEnabled;

    public DispatcherHost(CaptureHost capture, ISettingsStore store, AutoThrowTool throwTool)
    {
        _capture = capture;
        _store = store;
        _throwTool = throwTool;

        // 从持久化设置恢复开关状态，避免每次启动回退到默认值
        var shell = store.GetShellSettings();
        _autoThrowEnabled = shell.AutoThrowEnabled;
        _autoBattleEnabled = shell.AutoBattleEnabled;

        _capture.Changed += OnCaptureChanged;
    }

    /// <summary>当前场景（供覆盖层读取）。</summary>
    public GameScene CurrentScene { get; private set; } = GameScene.Unknown;

    /// <summary>调试叠层用：当前自动丢球管线。</summary>
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

    /// <summary>调度器任务状态（无任务时为 Idle），供覆盖层播种初始状态。</summary>
    public TaskState DispatcherState
    {
        get { lock (_gate) { return _task?.State ?? TaskState.Idle; } }
    }

    /// <summary>调度器任务状态迁移（覆盖层状态点/脱冲用）。</summary>
    public event EventHandler<TaskState>? TaskStateChanged;

    /// <summary>功能启用状态（由 UI 设置，变更即持久化）。</summary>
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

    private void PersistEnables()
    {
        var shell = _store.GetShellSettings();
        shell.AutoThrowEnabled = _autoThrowEnabled;
        shell.AutoBattleEnabled = _autoBattleEnabled;
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

    /// <summary>UI 切换功能开关后同步到 handler，并请求调度器重评激活态。</summary>
    public void SyncEnables()
    {
        SceneDispatcherRunningTask? task;
        lock (_gate)
        {
            if (_throwHandler is not null) _throwHandler.IsEnabled = AutoThrowEnabled;
            if (_battleHandler is not null) _battleHandler.IsEnabled = AutoBattleEnabled;
            task = _task;
        }

        // 仅改 IsEnabled 不够：调度器只在场景切换时参考该标志，
        // 稳定场景下关→开不会重新拉起 handler（票 21）
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
