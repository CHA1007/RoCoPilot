using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Detection;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class DetectionDebugPage : Page
{
    private const double BlitIntervalMs = 33;
    private const int MaxListedBoxes = 8;

    private static readonly Dictionary<string, (string Label, byte B, byte G, byte R)> s_classPresentation = new()
    {
        ["yaxiaxuexiong"] = ("月牙雪熊", 255, 170, 60),
        ["emolang"] = ("恶魔狼", 40, 140, 255),
    };

    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _readingsTimer;
    private readonly object _readingsGate = new();

    private ICaptureSource? _source;
    private OnnxYoloDetector? _detector;
    private StabilityGate? _gate;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private SemaphoreSlim? _frameSignal;
    private RollingFpsMeter _inferenceMeter = new();
    private int _generation;

    private IReadOnlyList<CaptureWindow> _windows = [];
    private WriteableBitmap? _bitmap;
    private byte[] _presentationBuffer = [];
    private int _blitBusy;
    private long _lastBlitTicks;
    private int _presentWidth;
    private int _presentHeight;
    private long _lastSequence = -1;
    private long _detectCount;
    private double _lastInferenceMs;
    private string _boxLines = "（无检出）";
    private string _stableLine = "稳定目标：—（门控未采信）";
    private string _paramsLine = "参数：—（读 settings.json tools.auto-throw）";

    public DetectionDebugPage(ISettingsStore store)
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

        OnnxYoloDetector? detector = null;
        ICaptureSource? source = null;
        try
        {
            var toolSettings = (AutoThrowSettings)_store.GetToolSettings(
                AutoThrowTool.ToolId, typeof(AutoThrowSettings), static () => new AutoThrowSettings());
            var options = toolSettings.ToDetectionOptions();

            SetStatus("正在装载检测器（ONNX 会话，首次约数百毫秒）…");
            detector = DetectorFactory.CreateOnnxYolo(options);
            var gate = new StabilityGate(options.StableFrames, options.StabilitySpreadPx, options.AssociationRadiusPx);

            var substring = WindowTitleBox.Text?.Trim();
            var captureOptions = new CaptureOptions
            {
                WindowTitleSubstring = string.IsNullOrEmpty(substring) ? null : substring,
            };
            SetStatus("正在启动捕获（回退链：窗口 WGC → 整屏 WGC → GDI）…");
            source = await CaptureSourceFactory.StartBestAvailableAsync(captureOptions);

            if (generation != _generation)
            {
                source.Stop();
                source.Dispose();
                detector.Dispose();
                return;
            }

            _source = source;
            _detector = detector;
            _gate = gate;
            _cts = new CancellationTokenSource();
            _frameSignal = new SemaphoreSlim(0);
            _inferenceMeter = new RollingFpsMeter();
            _lastSequence = -1;

            source.FrameArrived += OnFrameArrived;
            source.Stopped += OnSourceStopped;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "检测调试推理" };
            _worker.Start();

            var applied = detector.AppliedOptions;
            _paramsLine = "参数：conf " +
                $"{applied.ConfidenceThreshold:F2} · iou {applied.IouThreshold:F2} · 稳定 {applied.StableFrames} 帧 · " +
                $"白名单 {(applied.Whitelist.Count == 0 ? "全类" : string.Join("/", applied.Whitelist.Select(Label)))}";
            BackendText.Text = $"捕获后端：{source.BackendName}（检测 {detector.BackendName}）";
            SourceText.Text = $"目标：{source.SourceDescription}";
            StopButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            SetStatus("运行中");
            RefreshReadings();
        }
        catch (DetectionException ex)
        {
            source?.Dispose();
            detector?.Dispose();
            StartButton.IsEnabled = true;
            SetStatus($"检测器装载失败：{ex.Message}");
        }
        catch (CaptureException ex)
        {
            source?.Dispose();
            detector?.Dispose();
            StartButton.IsEnabled = true;
            SetStatus($"全链失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            source?.Dispose();
            detector?.Dispose();
            StartButton.IsEnabled = true;
            SetStatus($"意外失败：{ex.GetBaseException().Message}");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        TearDownPipeline();
        SetStatus("已停止");
    }

    private void OnFrameArrived(object? sender, EventArgs e)
    {
        try
        {
            _frameSignal?.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnSourceStopped(object? sender, CaptureStoppedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            SetStatus($"非请求式结束：{e.Reason}（最后一帧仍可查看）");
            StopButton.IsEnabled = false;
        });

    private void WorkerLoop()
    {
        var cts = _cts!;
        var signal = _frameSignal!;
        var source = _source!;
        var detector = _detector!;
        var gate = _gate!;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    signal.Wait(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (signal.Wait(0)) { }

                if (!source.TryGrabLatest(out var frame) || frame is null)
                {
                    continue;
                }

                if (frame.Sequence == _lastSequence)
                {
                    frame.Dispose();
                    continue;
                }

                _lastSequence = frame.Sequence;
                using (frame)
                {
                    if (frame.Pixels.IsEmpty)
                    {
                        continue;
                    }

                    var started = Stopwatch.GetTimestamp();
                    var boxes = detector.Detect(frame.Pixels, frame.Width, frame.Height);
                    var stable = gate.Update(boxes);
                    _lastInferenceMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    _inferenceMeter.Tick();
                    Interlocked.Increment(ref _detectCount);

                    UpdateReadoutLines(boxes, stable);
                    MaybeCompositeAndBlit(frame, boxes, stable);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() => SetStatus($"推理线程故障：{ex.GetBaseException().Message}"));
        }
    }

    private void MaybeCompositeAndBlit(CapturedFrame frame, IReadOnlyList<DetectedBox> boxes, IReadOnlyList<StableTarget> stable)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastBlitTicks);
        if (previous != 0 && (now - previous) * 1000.0 / Stopwatch.Frequency < BlitIntervalMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _blitBusy, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastBlitTicks, now);

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
            var (b, g, r) = ClassColor(box.ClassName);
            DrawRect(_presentationBuffer, width, height, box.X1, box.Y1, box.X2, box.Y2, b, g, r, thickness: 2);
        }

        foreach (var target in stable)
        {
            DrawRect(_presentationBuffer, width, height,
                target.Latest.X1 - 3, target.Latest.Y1 - 3, target.Latest.X2 + 3, target.Latest.Y2 + 3,
                b: 80, g: 230, r: 80, thickness: 3);
        }

        _presentWidth = width;
        _presentHeight = height;
        Dispatcher.InvokeAsync(BlitPresentation, DispatcherPriority.Background);
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
                LiveImage.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _presentationBuffer, width * 4, 0);
        }
        finally
        {
            Interlocked.Exchange(ref _blitBusy, 0);
        }
    }

    private void UpdateReadoutLines(IReadOnlyList<DetectedBox> boxes, IReadOnlyList<StableTarget> stable)
    {
        var builder = new StringBuilder();
        var shown = 0;
        foreach (var box in boxes)
        {
            if (shown++ >= MaxListedBoxes)
            {
                break;
            }

            builder.AppendLine(
                $"{Label(box.ClassName)} {box.Confidence:F2} @({box.CenterX:F0},{box.CenterY:F0}) {box.Width:F0}×{box.Height:F0}");
        }

        string stableLine;
        if (stable.Count == 0)
        {
            stableLine = "稳定目标：—（门控未采信）";
        }
        else
        {
            var first = stable[0];
            stableLine = $"稳定目标：{stable.Count} 个 · 首 {Label(first.Latest.ClassName)} " +
                $"@({first.MedianCenter.X:F0},{first.MedianCenter.Y:F0}) 连续 {first.ConsecutiveFrames} 帧";
        }

        lock (_readingsGate)
        {
            _boxLines = shown == 0 ? "（无检出）" : builder.ToString().TrimEnd();
            _stableLine = stableLine;
        }
    }

    private void RefreshReadings()
    {
        InferenceFpsText.Text = _source is null ? "— FPS" : $"{_inferenceMeter.CurrentFps:F0} FPS";
        InferenceMsText.Text = $"推理 {Volatile.Read(ref _lastInferenceMs):F1} ms/帧";
        DetectCountText.Text = $"累计推理 {Interlocked.Read(ref _detectCount)} 帧";
        CaptureFpsText.Text = _source is { } source
            ? $"捕获：{source.FramesPerSecond:F0} FPS · 分辨率 {(source.FrameWidth > 0 ? $"{source.FrameWidth}×{source.FrameHeight}" : "—")}"
            : "捕获：— FPS · 分辨率 —";
        ParamsText.Text = _paramsLine;

        lock (_readingsGate)
        {
            BoxListText.Text = _boxLines;
            StableText.Text = _stableLine;
        }
    }

    private void TearDownPipeline()
    {
        _generation++;

        if (_source is { } source)
        {
            source.FrameArrived -= OnFrameArrived;
            source.Stopped -= OnSourceStopped;
        }

        _cts?.Cancel();
        var worker = _worker;
        if (worker is not null && worker.IsAlive)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        _source?.Stop();
        _source?.Dispose();
        _detector?.Dispose();
        _cts?.Dispose();
        _frameSignal?.Dispose();

        _source = null;
        _detector = null;
        _gate = null;
        _worker = null;
        _cts = null;
        _frameSignal = null;
        StopButton.IsEnabled = false;
        BackendText.Text = "捕获后端：未启动";
        SourceText.Text = "目标：—";
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

    private static string Label(string className) =>
        s_classPresentation.TryGetValue(className, out var presentation) ? presentation.Label : className;

    private static (byte B, byte G, byte R) ClassColor(string className) =>
        s_classPresentation.TryGetValue(className, out var presentation)
            ? (presentation.B, presentation.G, presentation.R)
            : ((byte)200, (byte)200, (byte)200);

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

    private static void SetPixel(byte[] buffer, int pixelIndex, byte b, byte g, byte r)
    {
        var offset = pixelIndex * 4;
        buffer[offset] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = 0xFF;
    }
}
