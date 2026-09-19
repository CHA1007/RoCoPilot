using System.Windows;
using System.Windows.Controls;
using RocoPilot.Core;

namespace RocoPilot.Tools.Handpan;

public partial class HandpanConfigPanel : UserControl
{
    private readonly HandpanSettings _settings;
    private readonly Action _persist;
    private readonly HandpanTool _tool;

    public event Action<IRunningTask>? TaskCreated;

    public event Action? StopRequested;

    public HandpanConfigPanel(HandpanSettings settings, Action persist, HandpanTool tool)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _settings.SanitizeInPlace();

        InitializeComponent();
        DataContext = _settings;

        ScoreBox.LostFocus += OnScoreChanged;
        KeyChipsHost.LostFocus += OnKeyMapChanged;
        foreach (var toggle in new[] { AutoFitSwitch, FoldSwitch, SnapSwitch, LoopSwitch })
        {
            toggle.Click += (_, _) => _persist();
        }
    }

    private void OnScoreChanged(object sender, RoutedEventArgs e)
    {
        _settings.ScoreText = ScoreBox.Text;
        _persist();
    }

    private void OnKeyMapChanged(object sender, RoutedEventArgs e) => _persist();

    private void OnGenerateClick(object sender, RoutedEventArgs e) => Launch(HandpanTaskMode.Chart);

    private void OnProbeClick(object sender, RoutedEventArgs e) => Launch(HandpanTaskMode.Probe);

    private void OnPlayClick(object sender, RoutedEventArgs e) => Launch(HandpanTaskMode.Play);

    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void Launch(HandpanTaskMode mode)
    {
        var expression = ScoreBox.GetBindingExpression(TextBox.TextProperty);
        expression?.UpdateSource();
        var task = mode switch
        {
            HandpanTaskMode.Chart => _tool.RunChart(_settings),
            HandpanTaskMode.Probe => _tool.RunProbe(_settings),
            _ => _tool.Run(_settings),
        };
        task.EventRaised += OnTaskEvent;
        task.StateChanged += OnTaskStateChanged;
        TaskCreated?.Invoke(task);
    }

    internal void OnTaskEvent(object? sender, ToolEvent e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnTaskEvent(sender, e));
            return;
        }

        switch (e.Name)
        {
            case "chart":
                ChartBox.Text = e.Data?["text"] as string ?? string.Empty;
                var missing = e.Data?["missing"] as int? ?? 0;
                var notes = e.Data?["notes"] as int? ?? 0;
                StatusText.Text = $"已生成按键谱：{notes} 个音符" +
                                  (missing > 0 ? $"，{missing} 个音域外将跳过" : string.Empty);
                break;
            case "log":
                StatusText.Text = e.Data?["text"] as string ?? string.Empty;
                break;
            case "error":
                StatusText.Text = e.Data?["message"] as string ?? string.Empty;
                break;
        }
    }

    internal void OnTaskStateChanged(object? sender, TaskState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnTaskStateChanged(sender, state));
            return;
        }

        var busy = state is TaskState.Running or TaskState.Paused or TaskState.Arming or TaskState.Stopping;
        GenerateButton.IsEnabled = !busy;
        ProbeButton.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
    }
}
