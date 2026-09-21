using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RocoPilot.Core;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan;

public partial class HandpanConfigPanel : UserControl
{
    private const string MidiFilter = "MIDI 文件|*.mid;*.midi|所有文件|*.*";

    private const string TextFilter = "文本文件|*.txt";

    private readonly HandpanSettings _settings;
    private readonly Action _persist;
    private readonly HandpanTool _tool;
    private IReadOnlyList<string> _allScores = [];
    private IRunningTask? _task;
    private HandpanRunningTask? _progressSource;
    private bool _ready;
    private bool _paused;
    private bool _busy;
    private bool _handpanRunning;
    private bool _loadingProfile;
    private string? _scoreCacheName;
    private MidiScore? _scoreCache;

    public event Action<IRunningTask>? TaskCreated;

    public event Action? PauseRequested;

    public event Action? ResumeRequested;

    public event Action? StopRequested;

    public HandpanConfigPanel(HandpanSettings settings, Action persist, HandpanTool tool)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(tool);

        _settings = settings;
        _persist = persist;
        _tool = tool;
        _settings.SanitizeInPlace();

        InitializeComponent();
        DataContext = _settings;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            RefreshScores();
            return;
        }

        _ready = true;
        RefreshScores();
        SyncSpeedRows();
        foreach (var slider in FindAllChildren<Slider>(this))
        {
            slider.ValueChanged += OnSliderValueChanged;
        }

        foreach (var toggle in FindAllChildren<Wpf.Ui.Controls.ToggleSwitch>(this))
        {
            toggle.Checked += OnToggleChanged;
            toggle.Unchecked += OnToggleChanged;
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || sender is not Slider slider)
        {
            return;
        }

        slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
        if (ReferenceEquals(sender, TransposeSlider))
        {
            _ = UpdateTransposeSummaryAsync();
        }

        Commit();
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        if (!_loadingProfile && ReferenceEquals(sender, SpeedModeToggle))
        {
            ConvertSpeedForModeSwitch();
        }

        SyncSpeedRows();
        Commit();
    }

    private void ApplyScoreProfile()
    {
        var profile = _settings.ProfileOf(_settings.ScoreName);
        _loadingProfile = true;
        try
        {
            _settings.Transpose = profile.Transpose;
            _settings.MinIntervalMs = profile.MinIntervalMs;
            _settings.SpeedByPercent = profile.SpeedByPercent;
            _settings.SpeedPercent = profile.SpeedPercent;
            _settings.BpmOverride = profile.BpmOverride;
            TransposeSlider.Value = profile.Transpose;
            MinIntervalSlider.Value = profile.MinIntervalMs;
            SpeedModeToggle.IsChecked = profile.SpeedByPercent;
            SpeedPercentSlider.Value = profile.SpeedPercent;
            BpmSlider.Value = profile.BpmOverride;
            SyncSpeedRows();
        }
        finally
        {
            _loadingProfile = false;
        }
    }

    private void ConvertSpeedForModeSwitch()
    {
        var scoreBpm = _scoreCacheName == _settings.ScoreName && _scoreCache is not null
            ? _scoreCache.Meta.Bpm
            : 0;
        if (SpeedModeToggle.IsChecked == true)
        {
            SpeedPercentSlider.Value = _settings.SpeedPercentFor(scoreBpm);
        }
        else
        {
            BpmSlider.Value = _settings.BpmOverrideFor(scoreBpm);
        }
    }

    private async Task<MidiScore?> ScoreAsync()
    {
        var name = _settings.ScoreName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_scoreCacheName == name && _scoreCache is not null)
        {
            return _scoreCache;
        }

        var midiPath = _tool.Scores.Resolve(name);
        if (midiPath is null)
        {
            return null;
        }

        var score = await Task.Run(() => MidiParser.Parse(File.ReadAllBytes(midiPath)));
        if (_settings.ScoreName == name)
        {
            _scoreCacheName = name;
            _scoreCache = score;
        }

        return score;
    }

    private async Task UpdateTransposeSummaryAsync()
    {
        var semitones = (int)TransposeSlider.Value;
        MidiScore? score;
        try
        {
            score = await ScoreAsync();
        }
        catch (Exception)
        {
            HideTransposeSummary();
            return;
        }

        if (score is null)
        {
            HideTransposeSummary();
            return;
        }

        try
        {
            var keyMap = _settings.ToKeyMap();
            var fit = await Task.Run(() => MelodyFitting.Fit(
                MelodyPicker.Pick(score).Line,
                keyMap,
                score.Meta.Key,
                semitones));
            if ((int)TransposeSlider.Value != semitones)
            {
                return;
            }

            TransposeSummaryText.Text = HandpanStatus.TransposeSummary(fit, score.Meta.Key?.Transposed(semitones));
            TransposeSummaryText.Visibility = Visibility.Visible;
        }
        catch (MelodyPickException)
        {
            HideTransposeSummary();
        }
    }

    private void HideTransposeSummary()
    {
        TransposeSummaryText.Visibility = Visibility.Collapsed;
    }

    private void SyncSpeedRows()
    {
        var byPercent = _settings.SpeedByPercent;
        BpmRow.Visibility = byPercent ? Visibility.Collapsed : Visibility.Visible;
        BpmHint.Visibility = byPercent ? Visibility.Collapsed : Visibility.Visible;
        SpeedPercentRow.Visibility = byPercent ? Visibility.Visible : Visibility.Collapsed;
        SpeedPercentHint.Visibility = byPercent ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        ApplyFilter(((TextBox)sender).Text);
    }

    private void OnScoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ScoreList.SelectedItem is not string score)
        {
            return;
        }

        _settings.ScoreName = score;
        SongTitleText.Text = score;
        ApplyScoreProfile();
        _ = UpdateTransposeSummaryAsync();
        Commit();
        SyncTransport();
        if (!_busy)
        {
            Apply(new HandpanStatusView("待命"));
        }
    }

    private void OnScoreDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_busy && !_handpanRunning && ScoreList.SelectedItem is string)
        {
            Launch(HandpanMode.Play);
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = MidiFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var name = _tool.Scores.Import(dialog.FileName);
            RefreshScores(name);
            Commit();
            Apply(HandpanStatus.Imported());
        }
        catch (Exception ex)
        {
            Apply(new HandpanStatusView("导入失败", ex.GetBaseException().Message, HandpanStatusLevel.Critical));
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_tool.Scores.Root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", _tool.Scores.Root)
        {
            UseShellExecute = true,
        });
    }

    private void OnAutoFitClick(object sender, RoutedEventArgs e)
    {
        _settings.SanitizeInPlace();
        if (_tool.Scores.Resolve(_settings.ScoreName) is null)
        {
            Apply(new HandpanStatusView("适配失败", "请先选择曲谱", HandpanStatusLevel.Caution));
            return;
        }

        Apply(new HandpanStatusView("计算中"));
        AutoFitButton.IsEnabled = false;
        _ = AutoFitAsync();
    }

    private async Task AutoFitAsync()
    {
        try
        {
            var score = await ScoreAsync();
            if (score is null)
            {
                Apply(new HandpanStatusView("适配失败", "请先选择曲谱", HandpanStatusLevel.Caution));
                return;
            }

            var keyMap = _settings.ToKeyMap();
            var (semitones, fit) = await Task.Run(() =>
            {
                var line = MelodyLine.InTimeOrder(MelodyPicker.Pick(score).Line);
                var best = MelodyFitting.BestTransposition(line, keyMap, score.Meta.Key);
                return (best, MelodyFitting.Fit(line, keyMap, score.Meta.Key, best));
            });
            TransposeSlider.Value = semitones;
            Commit();
            Apply(new HandpanStatusView(
                "已适配",
                HandpanStatus.TransposeSummary(fit, score.Meta.Key?.Transposed(semitones))));
        }
        catch (Exception ex)
        {
            Apply(new HandpanStatusView("适配失败", ex.GetBaseException().Message, HandpanStatusLevel.Critical));
        }
        finally
        {
            AutoFitButton.IsEnabled = true;
        }
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => StepSelection(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => StepSelection(1);

    private void StepSelection(int delta)
    {
        var scores = ScoreList.ItemsSource as IReadOnlyList<string> ?? [];
        var target = ScoreList.SelectedIndex + delta;
        if (target < 0 || target >= scores.Count)
        {
            return;
        }

        ScoreList.SelectedIndex = target;
        ScoreList.ScrollIntoView(ScoreList.SelectedItem);
    }

    private void RefreshScores(string? preferred = null)
    {
        _allScores = _tool.Scores.List();
        ApplyFilter(ScoreSearchBox.Text);
        var scores = ScoreList.ItemsSource as IReadOnlyList<string> ?? [];
        var selected = preferred ?? _settings.ScoreName;
        ScoreList.SelectedItem = scores.Contains(selected) ? selected : scores.FirstOrDefault();
        if (ScoreList.SelectedItem is string current)
        {
            _settings.ScoreName = current;
            SongTitleText.Text = current;
        }
        else
        {
            SongTitleText.Text = "未选择曲谱";
        }
    }

    private void ApplyFilter(string text)
    {
        var keyword = text.Trim();
        var filtered = string.IsNullOrEmpty(keyword)
            ? _allScores
            : _allScores.Where(score => score.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ScoreList.ItemsSource = filtered;
        if (filtered.Count > 0)
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyHint.Text = _allScores.Count == 0 ? "曲谱库为空" : "没有匹配的曲谱";
        EmptyHint.Visibility = Visibility.Visible;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        _settings.SanitizeInPlace();
        var midiPath = _tool.Scores.Resolve(_settings.ScoreName);
        if (midiPath is null)
        {
            Apply(new HandpanStatusView("导出失败", "请先选择曲谱", HandpanStatusLevel.Caution));
            return;
        }

        var target = PickExportPath();
        if (target is null)
        {
            return;
        }

        var keyMap = _settings.ToKeyMap();
        var options = _settings.ToArrangement();
        Apply(new HandpanStatusView("生成中"));
        ExportButton.IsEnabled = false;
        _ = ExportAsync(midiPath, keyMap, options, target);
    }

    private string? PickExportPath()
    {
        var dialog = new SaveFileDialog { Filter = TextFilter };
        if (!string.IsNullOrWhiteSpace(_settings.ScoreName))
        {
            dialog.InitialDirectory = _tool.Scores.Root;
            dialog.FileName = _settings.ScoreName + "_按键谱.txt";
        }

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private async Task ExportAsync(
        string midiPath,
        KeyMap keyMap,
        ArrangementOptions options,
        string target)
    {
        try
        {
            var text = await Task.Run(() => BuildChartText(midiPath, keyMap, options));
            await File.WriteAllTextAsync(target, text);
            Apply(new HandpanStatusView("已导出", target));
        }
        catch (Exception ex)
        {
            Apply(new HandpanStatusView("导出失败", ex.GetBaseException().Message, HandpanStatusLevel.Critical));
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private static string BuildChartText(string midiPath, KeyMap keyMap, ArrangementOptions options)
    {
        var score = MidiParser.Parse(File.ReadAllBytes(midiPath));
        return HandpanArranger.Arrange(
            score,
            keyMap,
            Path.GetFileNameWithoutExtension(midiPath),
            options).Chart.Text;
    }

    private void OnProbeClick(object sender, RoutedEventArgs e) => Launch(HandpanMode.Probe);

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_handpanRunning)
        {
            if (_paused)
            {
                ResumeRequested?.Invoke();
            }
            else
            {
                PauseRequested?.Invoke();
            }

            return;
        }

        if (_busy)
        {
            return;
        }

        Launch(HandpanMode.Play);
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void Launch(HandpanMode mode)
    {
        Commit();
        _settings.Mode = mode;
        var task = _tool.Run(_settings);
        DetachTask();
        _task = task;
        _paused = false;
        HideProgress();
        if (task is HandpanRunningTask handpanTask)
        {
            _progressSource = handpanTask;
            handpanTask.ProgressChanged += OnTaskProgress;
        }

        task.EventRaised += OnTaskEvent;
        task.StateChanged += OnTaskStateChanged;
        TaskCreated?.Invoke(task);
    }

    public void SetBusy(bool anyTaskRunning, bool handpanTaskRunning)
    {
        _busy = anyTaskRunning;
        _handpanRunning = handpanTaskRunning;
        if (!_handpanRunning)
        {
            _paused = false;
        }

        SyncTransport();
    }

    private void SyncTransport()
    {
        var scores = ScoreList.ItemsSource as IReadOnlyList<string> ?? [];
        var canStep = !_busy && scores.Count > 0;
        PrevButton.IsEnabled = canStep && ScoreList.SelectedIndex > 0;
        NextButton.IsEnabled = canStep && ScoreList.SelectedIndex < scores.Count - 1;
        ProbeButton.IsEnabled = !_busy;
        PlayButton.IsEnabled = !_busy || _handpanRunning;
        StopButton.Visibility = _handpanRunning ? Visibility.Visible : Visibility.Collapsed;
        SetPlayButtonState();
    }

    private void SetPlayButtonState()
    {
        var playing = _handpanRunning && !_paused;
        if (PlayButton.Icon is Wpf.Ui.Controls.SymbolIcon symbolIcon)
        {
            symbolIcon.Symbol = playing ? Wpf.Ui.Controls.SymbolRegular.Pause24 : Wpf.Ui.Controls.SymbolRegular.Play24;
        }

        PlayButton.ToolTip = _handpanRunning
            ? (playing ? "暂停" : "恢复")
            : "开始演奏";
    }

    public void DetachTask()
    {
        var task = _task;
        var progressSource = _progressSource;
        _task = null;
        _progressSource = null;
        if (progressSource is not null)
        {
            progressSource.ProgressChanged -= OnTaskProgress;
        }

        if (task is null)
        {
            return;
        }

        task.EventRaised -= OnTaskEvent;
        task.StateChanged -= OnTaskStateChanged;
    }

    internal void OnTaskEvent(object? sender, ToolEvent toolEvent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnTaskEvent(sender, toolEvent));
            return;
        }

        var view = toolEvent.Payload switch
        {
            HandpanArranged arranged => HandpanStatus.Of(arranged),
            HandpanCountdown countdown => HandpanStatus.Of(countdown),
            HandpanPaused paused => HandpanStatus.Of(paused),
            HandpanResumed resumed => HandpanStatus.Of(resumed),
            HandpanRoundCompleted round => HandpanStatus.Of(round),
            HandpanCompleted completed => HandpanStatus.Of(completed),
            HandpanProbeKey probeKey => HandpanStatus.Of(probeKey),
            HandpanProbeCompleted probeCompleted => HandpanStatus.Of(probeCompleted),
            HandpanFaulted faulted => HandpanStatus.Of(faulted),
            _ => CoreEventView(toolEvent),
        };

        if (view is not null)
        {
            Apply(view);
        }
    }

    private static HandpanStatusView? CoreEventView(ToolEvent toolEvent) => toolEvent.Name switch
    {
        Arming.StepEvent => HandpanStatus.ArmingStep(toolEvent),
        Arming.FailedEvent => HandpanStatus.ArmingFailed(toolEvent),
        _ => null,
    };

    internal void OnTaskStateChanged(object? sender, TaskState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnTaskStateChanged(sender, state));
            return;
        }

        switch (state)
        {
            case TaskState.Paused:
                _paused = true;
                break;
            case TaskState.Idle:
                _paused = false;
                _handpanRunning = false;
                HideProgress();
                SyncTransport();
                break;
            case TaskState.Running:
                _paused = false;
                break;
        }

        SetPlayButtonState();
    }

    internal void OnTaskProgress(HandpanProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnTaskProgress(progress));
            return;
        }

        ProgressTimeRow.Visibility = Visibility.Visible;
        ProgressTrack.Value = progress.Ratio;
        ProgressTimeText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{progress.ElapsedSeconds:F1} / {progress.TotalSeconds:F1} 秒");
        ProgressRoundText.Text = progress.IsLooping
            ? string.Create(CultureInfo.InvariantCulture, $"第 {progress.RoundNumber} 遍")
            : string.Empty;
    }

    private void HideProgress()
    {
        ProgressTimeRow.Visibility = Visibility.Collapsed;
        ProgressTrack.Value = 0;
        ProgressTimeText.Text = string.Empty;
        ProgressRoundText.Text = string.Empty;
    }

    private void Apply(HandpanStatusView view)
    {
        StatusText.Text = view.Detail is null ? view.Headline : $"{view.Headline} · {view.Detail}";

        var brush = view.Level switch
        {
            HandpanStatusLevel.Caution => "SystemFillColorCautionBrush",
            HandpanStatusLevel.Critical => "SystemFillColorCriticalBrush",
            _ => "TextFillColorTertiaryBrush",
        };
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private void Commit()
    {
        _settings.SanitizeInPlace();
        _settings.SaveProfile(_settings.ScoreName);
        _persist();
    }

    private static IEnumerable<T> FindAllChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindAllChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
