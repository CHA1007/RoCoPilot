using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.Handpan;

namespace RocoPilot.Shell.Pages;

public partial class HandpanPage : Page
{
    private readonly ISettingsStore _store;
    private readonly RunningTaskHost _host;
    private readonly HandpanSettings _settings;
    private readonly HandpanConfigPanel _panel;

    public HandpanPage(HandpanTool tool, ISettingsStore store, RunningTaskHost host)
    {
        InitializeComponent();

        _store = store;
        _host = host;
        _settings = (HandpanSettings)store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);

        _panel = (HandpanConfigPanel)tool.CreateConfigPanel(_settings, Persist);
        _panel.TaskCreated += OnTaskCreated;
        _panel.PauseRequested += () => _host.RequestPause();
        _panel.ResumeRequested += () => _host.RequestResume();
        _panel.StopRequested += () => _host.RequestStop();
        ConfigHost.Content = _panel;

        _host.Changed += OnHostChanged;
        Loaded += OnPageLoaded;
        Unloaded += (_, _) => _panel.DetachTask();
        SyncBusyState();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.SetBinding(HeightProperty, new Binding("ViewportHeight")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1),
            Converter = new ViewportMinusMarginConverter(),
            ConverterParameter = RootGrid.Margin.Top + RootGrid.Margin.Bottom,
        });
    }

    private void OnTaskCreated(IRunningTask task) => _host.TryStart(task);

    private void OnHostChanged() => Dispatcher.InvokeAsync(SyncBusyState);

    private void SyncBusyState()
    {
        var current = _host.Current;
        _panel.SetBusy(current is not null, current?.ToolId == HandpanTool.ToolId);
    }

    private void Persist()
    {
        _store.SetToolSettings(HandpanTool.ToolId, _settings);
        _store.Save();
    }

    private sealed class ViewportMinusMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var delta = parameter is double offset ? offset : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
            return Math.Max(0, (double)value - delta);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
