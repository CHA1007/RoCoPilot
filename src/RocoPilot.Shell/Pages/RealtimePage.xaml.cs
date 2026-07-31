using System.Windows;
using System.Windows.Controls;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class RealtimePage : Page
{
    private readonly AutoThrowTool _tool;
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _taskHost;
    private readonly CaptureHost _capture;
    private readonly object _settings;
    private bool _updating;

    public RealtimePage(AutoThrowTool tool, ISettingsStore store, RunningTaskHost taskHost, CaptureHost capture)
    {
        InitializeComponent();

        _tool = tool;
        _store = store;
        _taskHost = taskHost;
        _capture = capture;
        _settings = store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);
        ConfigHost.Content = tool.CreateConfigPanel(_settings, Persist);

        Loaded += (_, _) =>
        {
            _taskHost.Changed += OnStateChanged;
            RefreshToggle();
        };
        Unloaded += (_, _) => _taskHost.Changed -= OnStateChanged;
    }

    private void OnAutoThrowToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (AutoThrowToggle.IsChecked == true)
        {
            ((AutoThrowSettings)_settings).InferenceDevice = _store.GetShellSettings().InferenceDevice;
            ((AutoThrowSettings)_settings).DetectionIntervalMs = _store.GetShellSettings().DetectionIntervalMs;
            if (_taskHost.TryStart(_tool, _settings))
            {
                if (!_capture.IsRunning)
                {
                    var title = ((AutoThrowSettings)_settings).WindowTitleSubstring;
                    _ = _capture.StartAsync(title);
                }
            }
            else
            {
                _updating = true;
                AutoThrowToggle.IsChecked = false;
                _updating = false;
            }
        }
        else
        {
            _taskHost.RequestStop();
        }
    }

    private void OnStateChanged() => Dispatcher.InvokeAsync(RefreshToggle);

    private void RefreshToggle()
    {
        _updating = true;
        AutoThrowToggle.IsChecked = _taskHost.Current is not null;
        _updating = false;
    }

    private void Persist()
    {
        _store.SetToolSettings(_tool.Id, _settings);
        _store.Save();
    }
}
