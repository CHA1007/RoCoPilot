using System.Windows;
using System.Windows.Controls;
using RocoPilot.Capture;
using RocoPilot.Shell.Dialogs;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Controls;

public partial class CaptureSetupCard : UserControl
{
    private bool _isRunning;
    private bool _isBusy;
    private string _runLabel = "开始";

    public CaptureSetupCard()
    {
        InitializeComponent();
        foreach (var (_, label) in CaptureBackendCatalog.Choices)
        {
            BackendBox.Items.Add($"捕获后端：{label}");
        }

        SyncRunButton();
    }

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;

    public string RunLabel
    {
        get => _runLabel;
        set
        {
            _runLabel = value;
            SyncRunButton();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            _isRunning = value;
            SyncRunButton();
            SyncSetupEditable();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            SyncRunButton();
        }
    }

    public string InitialTitle
    {
        get => WindowTitleBox.Text;
        set => WindowTitleBox.Text = value;
    }

    public string? TitleSubstring =>
        string.IsNullOrWhiteSpace(WindowTitleBox.Text) ? null : WindowTitleBox.Text.Trim();

    public CaptureBackendMode Backend => CaptureBackendCatalog.ModeAt(BackendBox.SelectedIndex);

    public CaptureOptions BuildCaptureOptions() => new()
    {
        WindowTitleSubstring = TitleSubstring,
        Backend = Backend,
    };

    private void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_isRunning)
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StartRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncRunButton()
    {
        if (RunButton is null)
        {
            return;
        }

        RunButton.Content = _isRunning ? "停止" : _runLabel;
        RunButton.Appearance = _isRunning
            ? Wpf.Ui.Controls.ControlAppearance.Caution
            : Wpf.Ui.Controls.ControlAppearance.Primary;
        RunButton.IsEnabled = !_isBusy;
    }

    private void SyncSetupEditable()
    {
        var editable = !_isRunning;
        WindowTitleBox.IsEnabled = editable;
        PickWindowButton.IsEnabled = editable;
        BackendBox.IsEnabled = editable;
    }

    private void OnPickWindowClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowPickerDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Picked is { } picked)
        {
            WindowTitleBox.Text = picked.Title;
        }
    }
}
