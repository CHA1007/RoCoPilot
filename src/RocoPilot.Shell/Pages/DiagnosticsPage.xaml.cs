using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public partial class DiagnosticsPage : Page
{
    private const double BlitIntervalMs = 33;
    private const int MaxListedBoxes = 8;
    private static readonly TimeSpan WorkerJoinTimeout = TimeSpan.FromSeconds(3);

    private static readonly Dictionary<string, (string Label, byte B, byte G, byte R)> s_classPresentation = new()
    {
        ["yaxiaxuexiong"] = ("月牙雪熊", 255, 170, 60),
        ["emolang"] = ("恶魔狼", 40, 140, 255),
    };

    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _readingsTimer;
    private readonly FrameBlitter _blitter = new();
    private readonly object _readingsGate = new();

    private ICaptureSource? _source;
    private OnnxYoloDetector? _detector;
    private StabilityGate? _gate;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private SemaphoreSlim? _frameSignal;
    private RollingFpsMeter _inferenceMeter = new();
    private int _generation;

    private int _blitBusy;
    private long _lastBlitTicks;
    private int _presentWidth;
    private int _presentHeight;
    private long _lastSequence = -1;
    private double _lastInferenceMs;
    private string _detectParamsLine = "参数：—（读 settings.json tools.auto-throw）";
    private string _boxLines = "（无检出）";
    private string _stableLine = "稳定目标：—（门控未采信）";

    public DiagnosticsPage(ISettingsStore store)
    {
        InitializeComponent();

        _store = store;
        SetupCard.StartRequested += OnStart;
        SetupCard.StopRequested += OnStop;

        _readingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _readingsTimer.Tick += (_, _) => RefreshReadings();

        Loaded += (_, _) => _readingsTimer.Start();
        Unloaded += (_, _) =>
        {
            _readingsTimer.Stop();
            TearDownPipeline();
        };
    }

    private async void OnStart(object? sender, EventArgs e)
    {
        TearDownPipeline();
        var generation = ++_generation;
        SetupCard.IsBusy = true;

        ICaptureSource? source = null;
        OnnxYoloDetector? detector = null;
        try
        {
            var toolSettings = (AutoThrowSettings)_store.GetToolSettings(
                AutoThrowTool.ToolId, typeof(AutoThrowSettings), static () => new AutoThrowSettings());
            var detectionOptions = toolSettings.ToDetectionOptions();

            SetStatus("正在启动捕获…");
            source = await CaptureSourceFactory.StartBestAvailableAsync(SetupCard.BuildCaptureOptions());

            SetStatus("正在装载检测器（ONNX 会话，首次约数百毫秒）…");
            detector = DetectorFactory.CreateOnnxYolo(detectionOptions);
            var gate = new StabilityGate(
                detectionOptions.StableFrames, detectionOptions.StabilitySpreadPx, detectionOptions.AssociationRadiusPx);

            if (generation != _generation)
            {
                CleanupLocals(source, detector);
                return;
            }

            _source = source;
            _detector = detector;
            _gate = gate;
            _cts = new CancellationTokenSource();
            _frameSignal = new SemaphoreSlim(0);
            _inferenceMeter = new RollingFpsMeter();
            _lastSequence = -1;

            source.FrameArrived += OnDetectFrameArrived;
            source.Stopped += OnSourceStopped;
            _worker = StartWorker(DetectionWorkerLoop, "检测调试推理");

            SourceText.Text = $"目标：{source.SourceDescription}";

            var applied = detector.AppliedOptions;
            _detectParamsLine = "参数：conf " +
                $"{applied.ConfidenceThreshold:F2} · iou {applied.IouThreshold:F2} · 稳定 {applied.StableFrames} 帧 · " +
                $"白名单 {(applied.Whitelist.Count == 0 ? "全类" : string.Join("/", applied.Whitelist.Select(Label)))}";

            SyncLiveVisibility();
            SetupCard.IsRunning = true;
            SetStatus("运行中");
            RefreshReadings();
        }
        catch (DetectionException ex)
        {
            CleanupLocals(source, detector);
            SetStatus($"检测器装载失败：{ex.Message}");
        }
        catch (CaptureException ex)
        {
            CleanupLocals(source, detector);
            SetStatus($"全链失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            CleanupLocals(source, detector);
            SetStatus($"意外失败：{ex.GetBaseException().Message}");
        }
        finally
        {
            SetupCard.IsBusy = false;
        }
    }

    private Thread StartWorker(ThreadStart body, string name)
    {
        var thread = new Thread(body) { IsBackground = true, Name = name };
        thread.Start();
        return thread;
    }

    private void OnStop(object? sender, EventArgs e)
    {
        TearDownPipeline();
        SetStatus("已停止");
    }

    private void OnDetectFrameArrived(object? sender, EventArgs e)
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
            SetupCard.IsRunning = false;
        });

    private void DetectionWorkerLoop()
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

        if (!_blitter.Prepare(frame))
        {
            Interlocked.Exchange(ref _blitBusy, 0);
            return;
        }

        var buffer = _blitter.Buffer;
        var width = frame.Width;
        var height = frame.Height;

        foreach (var box in boxes)
        {
            var (b, g, r) = ClassColor(box.ClassName);
            PixelPaint.DrawRect(buffer, width, height, box.X1, box.Y1, box.X2, box.Y2, b, g, r, thickness: 2);
        }

        foreach (var target in stable)
        {
            PixelPaint.DrawRect(buffer, width, height,
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

            _blitter.Blit(LiveImage, width, height);
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
        if (_source is { } source)
        {
            FpsText.Text = $"{source.FramesPerSecond:F0} FPS";
            SourceText.Text = source.FrameWidth > 0
                ? $"目标：{source.SourceDescription} · {source.FrameWidth}×{source.FrameHeight}"
                : $"目标：{source.SourceDescription}";
        }

        if (_detector is not null)
        {
            InferenceText.Text = _source is null
                ? "推理 —"
                : $"推理 {_inferenceMeter.CurrentFps:F0} FPS · {Volatile.Read(ref _lastInferenceMs):F1} ms/帧";
            DetectParamsText.Text = _detectParamsLine;
            lock (_readingsGate)
            {
                BoxListText.Text = _boxLines;
                StableText.Text = _stableLine;
            }
        }
    }

    private void SyncLiveVisibility() =>
        LiveImageCard.Visibility = _source is null ? Visibility.Collapsed : Visibility.Visible;

    private void TearDownPipeline()
    {
        _generation++;

        if (_source is { } live)
        {
            live.FrameArrived -= OnDetectFrameArrived;
            live.Stopped -= OnSourceStopped;
        }

        _cts?.Cancel();

        var worker = _worker;
        var source = _source;
        var detector = _detector;
        var cts = _cts;
        var signal = _frameSignal;

        _source = null;
        _detector = null;
        _gate = null;
        _worker = null;
        _cts = null;
        _frameSignal = null;

        SyncLiveVisibility();
        SetupCard.IsRunning = false;
        FpsText.Text = "— FPS";
        InferenceText.Text = "推理 —";
        SourceText.Text = "目标：—";

        if (worker is null && source is null)
        {
            cts?.Dispose();
            signal?.Dispose();
            return;
        }

        Task.Run(() =>
        {
            if (worker is not null && worker.IsAlive)
            {
                worker.Join(WorkerJoinTimeout);
            }

            source?.Stop();
            source?.Dispose();
            detector?.Dispose();
            cts?.Dispose();
            signal?.Dispose();
        });
    }

    private void CleanupLocals(ICaptureSource? source, OnnxYoloDetector? detector)
    {
        if (ReferenceEquals(source, _source))
        {
            TearDownPipeline();
            return;
        }

        source?.Dispose();
        detector?.Dispose();
    }

    private void SetStatus(string text) => StatusText.Text = $"状态：{text}";

    private static string Label(string className) =>
        s_classPresentation.TryGetValue(className, out var presentation) ? presentation.Label : className;

    private static (byte B, byte G, byte R) ClassColor(string className) =>
        s_classPresentation.TryGetValue(className, out var presentation)
            ? (presentation.B, presentation.G, presentation.R)
            : ((byte)200, (byte)200, (byte)200);
}
