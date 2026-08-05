using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Input;
using RocoPilot.Loop;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class CatchLoopDebugPage : Page
{
    private const int MaxEventLines = 12;
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WorkerJoinTimeout = TimeSpan.FromSeconds(3);

    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _readingsTimer;
    private readonly object _readingsGate = new();
    private readonly LinkedList<string> _eventLines = new();

    private ICaptureSource? _source;
    private OnnxYoloDetector? _detector;
    private StreamingTargetSensor? _sensor;
    private IInputDriver? _driver;
    private CatchLoopEngine? _engine;
    private JsonlEventSink? _sink;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private int _generation;
    private CatchLoopMode _mode = CatchLoopMode.MoveOnly;
    private bool _stalled;

    private IReadOnlyList<CaptureWindow> _windows = [];

    public CatchLoopDebugPage(ISettingsStore store)
    {
        InitializeComponent();

        _store = store;
        _readingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _readingsTimer.Tick += (_, _) => RefreshReadings();

        Loaded += (_, _) =>
        {
            RefreshWindowList();
            _readingsTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _readingsTimer.Stop();
            TearDownPipeline();
        };
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        TearDownPipeline();
        var generation = ++_generation;
        StartButton.IsEnabled = false;

        ICaptureSource? source = null;
        OnnxYoloDetector? detector = null;
        IInputDriver? driver = null;
        JsonlEventSink? sink = null;
        try
        {
            var toolSettings = (AutoThrowSettings)_store.GetToolSettings(
                AutoThrowTool.ToolId, typeof(AutoThrowSettings), static () => new AutoThrowSettings());
            var detectionOptions = toolSettings.ToDetectionOptions();
            var centeringOptions = toolSettings.ToCenteringOptions();

            SetStatus("正在装载检测器（ONNX 会话，首次约数百毫秒）…");
            detector = DetectorFactory.CreateOnnxYolo(detectionOptions);
            var gate = new StabilityGate(
                detectionOptions.StableFrames, detectionOptions.StabilitySpreadPx, detectionOptions.AssociationRadiusPx);

            var substring = WindowTitleBox.Text?.Trim();
            SetStatus("正在启动捕获（回退链：窗口 WGC → 整屏 WGC → GDI）…");
            source = await CaptureSourceFactory.StartBestAvailableAsync(new CaptureOptions
            {
                WindowTitleSubstring = string.IsNullOrEmpty(substring) ? null : substring,
            });

            driver = InputDriverFactory.Create();
            SetStatus("设备发现中：10 秒内动一下鼠标……（收得到事件＝驱动真在设备栈）");
            await Task.Run(() => driver.Arm(DiscoverTimeout));

            if (generation != _generation)
            {
                source.Stop();
                source.Dispose();
                detector.Dispose();
                driver.Dispose();
                return;
            }

            var logPath = Path.Combine(
                RocoPaths.LogsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"), "events.jsonl");
            sink = new JsonlEventSink(logPath);

            var sensor = new StreamingTargetSensor(source, detector, gate);
            sensor.Start();
            var controller = new CenteringController(centeringOptions, sensor, driver);
            var counters = new CatchCounters();
            var bus = new CatchEventBus(counters, sink);
            var engine = new CatchLoopEngine(new CatchLoopOptions(), _mode, sensor, driver, controller, bus);
            bus.EventRaised += OnBusEvent;

            _source = source;
            _detector = detector;
            _driver = driver;
            _sensor = sensor;
            _sink = sink;
            _engine = engine;
            _cts = new CancellationTokenSource();
            _stalled = false;

            for (var i = 3; i > 0; i--)
            {
                if (generation != _generation)
                {
                    return;
                }

                SetStatus($"{i}…… 现在去点游戏窗口聚焦，随后自动开跑（{_mode} 档）");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            if (generation != _generation)
            {
                return;
            }

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "捕捉循环" };
            _worker.Start();

            BackendText.Text = $"捕获后端：{source.BackendName} · 输入后端：{driver.BackendName}（检测 {detector.BackendName}）";
            LogPathText.Text = $"会话日志：{logPath}";
            StopButton.IsEnabled = true;
            PauseResumeButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            SetStatus(_mode == CatchLoopMode.Live
                ? "运行中：LIVE 全量真投——人在场，见异色 / 弹窗先按暂停"
                : $"运行中：{_mode} 档");
            RefreshReadings();
        }
        catch (InputDriverException ex)
        {
            CleanupLocals(source, detector, driver, sink);
            StartButton.IsEnabled = true;
            SetStatus($"设备发现失败：{ex.Message}");
        }
        catch (DetectionException ex)
        {
            CleanupLocals(source, detector, driver, sink);
            StartButton.IsEnabled = true;
            SetStatus($"检测器装载失败：{ex.Message}");
        }
        catch (CaptureException ex)
        {
            CleanupLocals(source, detector, driver, sink);
            StartButton.IsEnabled = true;
            SetStatus($"全链失败：{ex.Message}");
        }
        catch (LoopException ex)
        {
            CleanupLocals(source, detector, driver, sink);
            StartButton.IsEnabled = true;
            SetStatus($"循环前置不满足：{ex.Message}");
        }
        catch (Exception ex)
        {
            CleanupLocals(source, detector, driver, sink);
            StartButton.IsEnabled = true;
            SetStatus($"意外失败：{ex.GetBaseException().Message}");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        TearDownPipeline();
        SetStatus("已停止（停令在尝试边界收完当前一发宏即生效）");
    }

    private void OnPauseResumeClick(object sender, RoutedEventArgs e)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        if (engine.State == CatchLoopState.Paused)
        {
            engine.Resume("page");
            SetStatus("已恢复：重新扫描选目标（不恢复旧锚）");
        }
        else
        {
            engine.Pause("page");
            SetStatus("暂停中：尝试边界收完当前一发宏后零输入（~1.3s）");
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        _mode = ModeCombo.SelectedIndex switch
        {
            1 => CatchLoopMode.Live,
            2 => CatchLoopMode.DryRun,
            _ => CatchLoopMode.MoveOnly,
        };
    }

    private void WorkerLoop()
    {
        try
        {
            _engine!.Run(_cts!.Token);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() => SetStatus($"循环故障：{ex.GetBaseException().Message}"));
        }
    }

    private void OnBusEvent(object? sender, ToolEvent toolEvent)
    {
        var payload = toolEvent.Data is { } data
            ? string.Join(" ", data.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"))
            : string.Empty;
        var line = $"[{toolEvent.Timestamp:HH:mm:ss}] {toolEvent.Name} {payload}".TrimEnd();

        lock (_readingsGate)
        {
            _eventLines.AddFirst(line);
            while (_eventLines.Count > MaxEventLines)
            {
                _eventLines.RemoveLast();
            }

            if (toolEvent.Name == "stall_alert")
            {
                _stalled = true;
            }
            else if (toolEvent.Name is "session_start" or "target_acquired"
                || (toolEvent.Name == "settled" && Equals(toolEvent.Data?.GetValueOrDefault("result"), "gone")))
            {
                _stalled = false;
            }
        }
    }

    private void RefreshReadings()
    {
        var engine = _engine;
        if (engine is null)
        {
            StateText.Text = "当前状态：Idle · —";
            lock (_readingsGate)
            {
                StallText.Visibility = Visibility.Collapsed;
                EventsText.Text = _eventLines.Count == 0 ? "（无事件）" : string.Join("\n", _eventLines);
            }

            return;
        }

        var snapshot = engine.Bus.Counters.Snapshot();
        StateText.Text = $"当前状态：{StateString(snapshot.State)} · {PhaseString(engine.Phase)}";
        var rate = snapshot.CenteringRate is { } r ? $"{r:P0}" : "—";
        StatsText.Text = $"投掷 {snapshot.Throws} · 了结 {snapshot.Settled} · " +
            $"投每小时 {snapshot.ThrowsPerHour:F0} · 居中率 {rate}";
        TimeText.Text = $"运行 {snapshot.RunDuration:hh\\:mm\\:ss} · 休息 {snapshot.RestDuration:hh\\:mm\\:ss} · " +
            $"距上次了结 {(int)snapshot.SinceLastSettle.TotalSeconds}s";

        PauseResumeButton.Content = engine.State == CatchLoopState.Paused ? "恢复" : "暂停";

        lock (_readingsGate)
        {
            StallText.Visibility = _stalled ? Visibility.Visible : Visibility.Collapsed;
            EventsText.Text = _eventLines.Count == 0 ? "（无事件）" : string.Join("\n", _eventLines);
        }
    }

    private void TearDownPipeline()
    {
        _generation++;

        _cts?.Cancel();
        var worker = _worker;
        if (worker is not null && worker.IsAlive)
        {
            worker.Join(WorkerJoinTimeout);
        }

        if (_engine is { } engine)
        {
            engine.Bus.EventRaised -= OnBusEvent;
            engine.Dispose();
        }

        _sensor?.Dispose();
        _source?.Stop();
        _source?.Dispose();
        _detector?.Dispose();
        _driver?.Dispose();
        _sink?.Dispose();
        _cts?.Dispose();

        _engine = null;
        _sensor = null;
        _source = null;
        _detector = null;
        _driver = null;
        _sink = null;
        _worker = null;
        _cts = null;
        StopButton.IsEnabled = false;
        PauseResumeButton.IsEnabled = false;
        BackendText.Text = "捕获后端：未启动";
    }

    private void CleanupLocals(ICaptureSource? source, OnnxYoloDetector? detector, IInputDriver? driver, JsonlEventSink? sink)
    {
        if (ReferenceEquals(source, _source))
        {
            TearDownPipeline();
            return;
        }

        source?.Dispose();
        detector?.Dispose();
        driver?.Dispose();
        sink?.Dispose();
    }

    private void OnRefreshWindowsClick(object sender, RoutedEventArgs e) => RefreshWindowList();

    private void RefreshWindowList()
    {
        try
        {
            _windows = WindowFinder.ListVisibleWindows();
        }
        catch (Exception ex)
        {
            SetStatus($"枚举窗口失败：{ex.GetBaseException().Message}");
            return;
        }

        WindowList.Items.Clear();
        foreach (var window in _windows)
        {
            WindowList.Items.Add($"{window.Title}（0x{window.Handle:X}）");
        }

        SetStatus($"可见窗口 {_windows.Count} 个；选中即填入标题子串");
    }

    private void OnWindowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (WindowList.SelectedIndex is >= 0 and var index && index < _windows.Count)
        {
            WindowTitleBox.Text = _windows[index].Title;
        }
    }

    private void SetStatus(string text) => StatusText.Text = $"状态：{text}";

    private static string StateString(CatchLoopState state) => state switch
    {
        CatchLoopState.Running => "Running",
        CatchLoopState.Paused => "Paused",
        _ => "Idle",
    };

    private static string PhaseString(CatchPhase phase) => phase switch
    {
        CatchPhase.Scanning => "SCANNING",
        CatchPhase.Centering => "CENTERING",
        CatchPhase.Throwing => "THROWING",
        CatchPhase.Settling => "SETTLING",
        _ => "—",
    };

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        System.Collections.IEnumerable enumerable and not string =>
            "[" + string.Join(",", enumerable.Cast<object?>()) + "]",
        double d => d.ToString("F3"),
        _ => value.ToString() ?? "",
    };
}
