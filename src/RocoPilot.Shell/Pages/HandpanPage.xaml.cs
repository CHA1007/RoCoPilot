using System.Windows.Controls;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.Handpan;

namespace RocoPilot.Shell.Pages;

public partial class HandpanPage : Page
{
    private readonly HandpanTool _tool;
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _host;
    private readonly HandpanSettings _settings;

    public HandpanPage(HandpanTool tool, ISettingsStore store, RunningTaskHost host)
    {
        InitializeComponent();

        _tool = tool;
        _store = store;
        _host = host;
        _settings = (HandpanSettings)store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);

        var panel = (HandpanConfigPanel)tool.CreateConfigPanel(_settings, Persist);
        panel.TaskCreated += OnTaskCreated;
        panel.StopRequested += () => _host.RequestStop();
        ConfigHost.Content = panel;
    }

    private void OnTaskCreated(IRunningTask task)
    {
        if (!_host.TryStart(task))
        {
            (task as IDisposable)?.Dispose();
        }
    }

    private void Persist()
    {
        _store.SetToolSettings(HandpanTool.ToolId, _settings);
        _store.Save();
    }
}
