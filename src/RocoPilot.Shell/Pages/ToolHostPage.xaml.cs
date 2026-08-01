using System.Windows;
using System.Windows.Controls;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Pages;

public partial class ToolHostPage : Page
{
    private readonly ITool _tool;
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _taskHost;
    private readonly object _settings;
    private IRunningTask? _observed;
    private string? _armingHint;
    private string? _armingFailure;

    protected ToolHostPage(ITool tool, ISettingsStore store, RunningTaskHost taskHost)
    {
        InitializeComponent();

        _tool = tool;
        _store = store;
        _taskHost = taskHost;
        _settings = store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);

        TitleText.Text = tool.DisplayName;
        SubtitleText.Text = "切出游戏自动暂停，切回自动继续。";
        ConfigPanelHost.Content = tool.CreateConfigPanel(_settings, Persist);

        Loaded += (_, _) =>
        {
            _taskHost.Changed += OnTaskHostChanged;
            Observe(_taskHost.Current);
            RefreshStatus();
        };
        Unloaded += (_, _) =>
        {
            _taskHost.Changed -= OnTaskHostChanged;
            Observe(null);
        };
    }


    private void Persist()
    {
        _store.SetToolSettings(_tool.Id, _settings);
        _store.Save();
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        _armingHint = null;
        _armingFailure = null;
        try
        {
            if (_taskHost.TryStart(_tool, _settings))
            {
                Observe(_taskHost.Current);
            }
        }
        catch (Exception)
        {
            // 截图器未启动等前置条件不满足时静默忽略，覆盖层不显示即无提示
        }

        RefreshStatus();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e) => _taskHost.RequestPause();

    private void OnResumeClick(object sender, RoutedEventArgs e) => _taskHost.RequestResume();

    private void OnStopClick(object sender, RoutedEventArgs e) => _taskHost.RequestStop();

    private void OnTaskHostChanged() => OnUi(() =>
    {
        Observe(_taskHost.Current);
        RefreshStatus();
    });

    private void OnTaskStateChanged(object? sender, TaskState state) => OnUi(RefreshStatus);

    private void OnToolEvent(object? sender, ToolEvent toolEvent) => OnUi(() => HandleToolEventUi(toolEvent));

    private void HandleToolEventUi(ToolEvent toolEvent)
    {
        switch (toolEvent.Name)
        {
            case "arming_step":
            {
                var step = toolEvent.Data?["step"];
                var hint = toolEvent.Data?["hint"];
                _armingHint = "自检中（" + step + "）：" + hint;
                _armingFailure = null;
                RefreshStatus();
                break;
            }

            case "arming_failed":
            {
                var step = toolEvent.Data?["step"];
                var error = toolEvent.Data?["error"];
                var remedy = toolEvent.Data?["remedy"];
                _armingHint = null;
                _armingFailure = "启动失败（" + step + "）：" + error + "。" + remedy;
                RefreshStatus();
                break;
            }
        }
    }

    private void Observe(IRunningTask? task)
    {
        if (ReferenceEquals(_observed, task))
        {
            return;
        }

        if (_observed is not null)
        {
            _observed.StateChanged -= OnTaskStateChanged;
            _observed.EventRaised -= OnToolEvent;
        }

        _observed = task;
        if (task is not null)
        {
            task.StateChanged += OnTaskStateChanged;
            task.EventRaised += OnToolEvent;
        }
    }

    private void RefreshStatus()
    {
        var state = _taskHost.Current?.State ?? TaskState.Idle;
        ArmingHintText.Text = _armingHint;
        ArmingHintText.Visibility = state == TaskState.Arming && _armingHint is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ArmingFailureText.Text = _armingFailure;
        ArmingFailureText.Visibility = state == TaskState.Idle && _armingFailure is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartButton.IsEnabled = _taskHost.Current is null;
        PauseButton.IsEnabled = state == TaskState.Running;
        ResumeButton.IsEnabled = state == TaskState.Paused;
        StopButton.IsEnabled = state != TaskState.Idle;
    }


    private void OnUi(Action action) => Dispatcher.InvokeAsync(action);
}
