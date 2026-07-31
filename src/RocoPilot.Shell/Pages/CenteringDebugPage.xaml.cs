using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

public partial class CenteringDebugPage : Page
{
    private const int MaxAttemptLines = 12;
    private const int MaxEventLines = 8;
    private const int AttemptGapMs = 1000;
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromSeconds(10);

    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _readingsTimer;
    private readonly object _readingsGate = new();
    private readonly LinkedList<string> _attemptLines = new();
    private readonly LinkedList<string> _eventLines = new();

    private ICaptureSource? _source;
    private OnnxYoloDetector? _detector;
    private StreamingTargetSensor? _sensor;
    private CenteringController? _controller;
    private IInputDriver? _driver;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private int _generation;
    private string _backend = InputDriverFactory.Interception;

    private IReadOnlyList<CaptureWindow> _windows = [];
    private WriteableBitmap? _bitmap;
    private byte[] _presentationBuffer = [];
    private int _blitBusy;
    private int _presentWidth;
    private int _presentHeight;
    private int _attempts;
    private int _centered;
    private string _paramsLine = "参数：—（读 settings.json tools.auto-throw）";

    public CenteringDebugPage(ISettingsStore store)
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

            driver = InputDriverFactory.Create(_backend);
            SetStatus(_backend == InputDriverFactory.Interception
                ? "设备发现中：10 秒内动一下鼠标……（收得到事件＝驱动真在设备栈）"
                : "sendinput 无设备栈，设备发现即刻通过。");
            await Task.Run(() => driver.Arm(DiscoverTimeout));

            if (generation != _generation)
            {
                source.Stop();
                source.Dispose();
                detector.Dispose();
                driver.Dispose();
                return;
            }

            var sensor = new StreamingTargetSensor(source, detector, gate);
            sensor.Start();
            var controller = new CenteringController(centeringOptions, sensor, driver);
            controller.EventRaised += OnControllerEvent;

            _source = source;
            _detector = detector;
            _driver = driver;
            _sensor = sensor;
            _controller = controller;
            _cts = new CancellationTokenSource();

            var applied = controller.AppliedOptions;
            _paramsLine = $"参数：容差 {applied.TolerancePx:F0}px · 步数上限 {applied.MaxSteps} · " +
                $"重检 {applied.RecheckMs}ms · 单步封顶 {applied.MaxStepCounts} counts · 回退除数 {applied.FallbackDivisor:F0}";

            for (var i = 3; i > 0; i--)
            {
                if (generation != _generation)
                {
                    return;
                }

                SetStatus($"{i}…… 现在去点游戏窗口聚焦，随后自动开始转镜头（move-only，不发键）");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            if (generation != _generation)
            {
                return;
            }

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "居中调试" };
            _worker.Start();

            BackendText.Text = $"捕获后端：{source.BackendName} · 输入后端：{driver.BackendName}（检测 {detector.BackendName}）";
            StopButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            SetStatus("运行中：move-only 居中循环（零球耗）");
            RefreshReadings();
        }
        catch (InputDriverException ex)
        {
            CleanupLocals(source, detector, driver);
            StartButton.IsEnabled = true;
            SetStatus($"设备发现失败：{ex.Message}");
        }
        catch (DetectionException ex)
        {
            CleanupLocals(source, detector, driver);
            StartButton.IsEnabled = true;
            SetStatus($"检测器装载失败：{ex.Message}");
        }
        catch (CaptureException ex)
        {
            CleanupLocals(source, detector, driver);
            StartButton.IsEnabled = true;
            SetStatus($"全链失败：{ex.Message}");
        }
        catch (LoopException ex)
        {
            CleanupLocals(source, detector, driver);
            StartButton.IsEnabled = true;
            SetStatus($"居中前置不满足：{ex.Message}");
        }
        catch (Exception ex)
        {
            CleanupLocals(source, detector, driver);
            StartButton.IsEnabled = true;
            SetStatus($"意外失败：{ex.GetBaseException().Message}");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        TearDownPipeline();
        SetStatus("已停止");
    }

    private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
    {
        _backend = BackendCombo.SelectedIndex == 1 ? InputDriverFactory.SendInput : InputDriverFactory.Interception;
    }

    private void WorkerLoop()
    {
        var cts = _cts!;
        var controller = _controller!;
        var source = _source!;
        var detector = _detector!;
        var seq = 0;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                CenteringResult result;
                try
                {
                    result = controller.RunOnce(new CenteringRequest(), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                seq++;
                RecordAttempt(seq, result);
                TrySnapshot(source, detector);

                if (cts.Token.WaitHandle.WaitOne(AttemptGapMs))
                {
                    break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() => SetStatus($"居中线程故障：{ex.GetBaseException().Message}"));
        }
    }

    private void RecordAttempt(int seq, CenteringResult result)
    {
        var verdict = result.Outcome switch
        {
            CenteringOutcome.Centered => "居中✔",
            CenteringOutcome.Lost => "丢失",
            _ => "步数尽",
        };
        var ppc = result.PixelsPerCount is { } p ? p.ToString("F3") : "—";
        var target = result.Target is { } t ? $" [{t.ClassName} {t.Confidence:F2}]" : "";
        var line = $"#{seq} {verdict} steps={result.Steps} 残差={result.ResidualPx:F1}px " +
            $"calib={result.CalibrationSource.EventString()} ppc={ppc}{target}";

        lock (_readingsGate)
        {
            _attemptLines.AddFirst(line);
            while (_attemptLines.Count > MaxAttemptLines)
            {
                _attemptLines.RemoveLast();
            }

            _attempts++;
            if (result.Outcome == CenteringOutcome.Centered)
            {
                _centered++;
            }
        }
    }

    private void OnControllerEvent(object? sender, ToolEvent toolEvent)
    {
        var payload = toolEvent.Data is { } data
            ? string.Join(" ", data.Select(kv => $"{kv.Key}={kv.Value}"))
            : string.Empty;
        var line = $"[{toolEvent.Timestamp:HH:mm:ss}] {toolEvent.Name} {payload}".TrimEnd();

        lock (_readingsGate)
        {
            _eventLines.AddFirst(line);
            while (_eventLines.Count > MaxEventLines)
            {
                _eventLines.RemoveLast();
            }
        }
    }

    private void TrySnapshot(ICaptureSource source, OnnxYoloDetector detector)
    {
        if (!source.TryGrabLatest(out var frame) || frame is null)
        {
            return;
        }

        using (frame)
        {
            if (frame.Pixels.IsEmpty || Interlocked.CompareExchange(ref _blitBusy, 1, 0) != 0)
            {
                return;
            }

            var boxes = detector.Detect(frame.Pixels, frame.Width, frame.Height);
            var width = frame.Width;
            var height = frame.Height;
            var length = frame.Pixels.Length;
            if (_presentationBuffer.Length < length)
            {
                _presentationBuffer = new byte[length];
            }

            frame.Pixels.CopyTo(_presentationBuffer);
            for (var i = 3; i < length; i += 4)
            {
                _presentationBuffer[i] = 0xFF;
            }

            foreach (var box in boxes)
            {
                DrawRect(_presentationBuffer, width, height, box.X1, box.Y1, box.X2, box.Y2,
                    b: 80, g: 230, r: 80, thickness: 2);
            }

            DrawCrosshair(_presentationBuffer, width, height, width / 2, height / 2, radius: 20, b: 60, g: 60, r: 255);

            _presentWidth = width;
            _presentHeight = height;
            Dispatcher.InvokeAsync(BlitPresentation, DispatcherPriority.Background);
        }
    }

    private void BlitPresentation()
    {
        try
        {
            var width = _presentWidth;
            var height = _presentHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                SnapshotImage.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _presentationBuffer, width * 4, 0);
        }
        finally
        {
            Interlocked.Exchange(ref _blitBusy, 0);
        }
    }

    private void RefreshReadings()
    {
        var controller = _controller;
        if (controller is not null)
        {
            var buckets = controller.Cache.Buckets;
            PpcText.Text = buckets.Count == 0
                ? "ppc 缓存：无（首次居中前自标定）"
                : "ppc 缓存：" + string.Join(" · ", buckets.Select(kv => $"{kv.Key}→{kv.Value:F3}"));
        }

        ParamsText.Text = _paramsLine;
        lock (_readingsGate)
        {
            StatsText.Text = _attempts == 0
                ? "尝试 0 · 居中 0 · 居中率 —"
                : $"尝试 {_attempts} · 居中 {_centered} · 居中率 {100.0 * _centered / _attempts:F0}%";
            AttemptsText.Text = _attemptLines.Count == 0 ? "（尚未尝试）" : string.Join("\n", _attemptLines);
            EventsText.Text = _eventLines.Count == 0 ? "（无事件）" : string.Join("\n", _eventLines);
        }
    }

    private void TearDownPipeline()
    {
        _generation++;

        if (_controller is { } controller)
        {
            controller.EventRaised -= OnControllerEvent;
        }

        _cts?.Cancel();
        var worker = _worker;
        if (worker is not null && worker.IsAlive)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        _sensor?.Dispose();
        _source?.Stop();
        _source?.Dispose();
        _detector?.Dispose();
        _driver?.Dispose();
        _cts?.Dispose();

        _sensor = null;
        _source = null;
        _detector = null;
        _driver = null;
        _controller = null;
        _worker = null;
        _cts = null;
        StopButton.IsEnabled = false;
        BackendText.Text = "捕获后端：未启动";
    }

    private void CleanupLocals(ICaptureSource? source, OnnxYoloDetector? detector, IInputDriver? driver)
    {
        if (ReferenceEquals(source, _source))
        {
            TearDownPipeline();
            return;
        }

        source?.Dispose();
        detector?.Dispose();
        driver?.Dispose();
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

    private static void DrawRect(
        byte[] buffer, int width, int height,
        float x1, float y1, float x2, float y2,
        byte b, byte g, byte r, int thickness)
    {
        var left = Math.Clamp((int)x1, 0, width - 1);
        var right = Math.Clamp((int)x2, 0, width - 1);
        var top = Math.Clamp((int)y1, 0, height - 1);
        var bottom = Math.Clamp((int)y2, 0, height - 1);
        if (left > right || top > bottom)
        {
            return;
        }

        for (var t = 0; t < thickness; t++)
        {
            var topRow = Math.Clamp(top + t, 0, height - 1);
            var bottomRow = Math.Clamp(bottom - t, 0, height - 1);
            for (var x = left; x <= right; x++)
            {
                SetPixel(buffer, topRow * width + x, b, g, r);
                SetPixel(buffer, bottomRow * width + x, b, g, r);
            }

            var leftCol = Math.Clamp(left + t, 0, width - 1);
            var rightCol = Math.Clamp(right - t, 0, width - 1);
            for (var y = top; y <= bottom; y++)
            {
                SetPixel(buffer, y * width + leftCol, b, g, r);
                SetPixel(buffer, y * width + rightCol, b, g, r);
            }
        }
    }

    private static void DrawCrosshair(byte[] buffer, int width, int height, int cx, int cy, int radius, byte b, byte g, byte r)
    {
        for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
        {
            SetPixel(buffer, cy * width + x, b, g, r);
        }

        for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); y++)
        {
            SetPixel(buffer, y * width + cx, b, g, r);
        }
    }

    private static void SetPixel(byte[] buffer, int pixelIndex, byte b, byte g, byte r)
    {
        var offset = pixelIndex * 4;
        buffer[offset] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = 0xFF;
    }
}
