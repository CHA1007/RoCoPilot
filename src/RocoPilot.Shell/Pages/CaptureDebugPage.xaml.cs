using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RocoPilot.Capture;

namespace RocoPilot.Shell.Pages;

public partial class CaptureDebugPage : Page
{
    private const double RenderIntervalMs = 40;

    private readonly DispatcherTimer _readingsTimer;
    private ICaptureSource? _source;
    private IReadOnlyList<CaptureWindow> _windows = [];
    private WriteableBitmap? _bitmap;
    private byte[] _presentationBuffer = [];
    private long _lastRenderTicks;

    public CaptureDebugPage()
    {
        InitializeComponent();

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
            TearDownSource();
        };
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        TearDownSource();

        var substring = WindowTitleBox.Text?.Trim();
        var options = new CaptureOptions
        {
            WindowTitleSubstring = string.IsNullOrEmpty(substring) ? null : substring,
        };

        StartButton.IsEnabled = false;
        SetStatus("正在启动（回退链：窗口 WGC → 整屏 WGC → GDI）…");
        try
        {
            var source = await CaptureSourceFactory.StartBestAvailableAsync(options);
            _source = source;
            source.FrameArrived += OnFrameArrived;
            source.Stopped += OnSourceStopped;
            BackendText.Text = $"后端：{source.BackendName}";
            SourceText.Text = $"目标：{source.SourceDescription}";
            StopButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            SetStatus("运行中");
            RefreshReadings();
        }
        catch (CaptureException ex)
        {
            StartButton.IsEnabled = true;
            SetStatus($"全链失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            StartButton.IsEnabled = true;
            SetStatus($"意外失败：{ex.GetBaseException().Message}");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        TearDownSource();
        SetStatus("已停止");
    }

    private void OnFrameArrived(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastRenderTicks);
        if (previous != 0 && (now - previous) * 1000.0 / Stopwatch.Frequency < RenderIntervalMs)
        {
            return;
        }

        Interlocked.Exchange(ref _lastRenderTicks, now);
        Dispatcher.InvokeAsync(RenderLatestFrame, DispatcherPriority.Background);
    }

    private void OnSourceStopped(object? sender, CaptureStoppedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            SetStatus($"非请求式结束：{e.Reason}（最后一帧仍可查看）");
            StopButton.IsEnabled = false;
        });

    private void RenderLatestFrame()
    {
        if (_source is not { } source || !source.TryGrabLatest(out var frame) || frame is null)
        {
            return;
        }

        using (frame)
        {
            var width = frame.Width;
            var height = frame.Height;
            var pixels = frame.Pixels;
            if (pixels.IsEmpty)
            {
                return;
            }

            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                LiveImage.Source = _bitmap;
            }

            var length = pixels.Length;
            if (_presentationBuffer.Length < length)
            {
                _presentationBuffer = new byte[length];
            }

            pixels.CopyTo(_presentationBuffer);
            for (var i = 3; i < length; i += 4)
            {
                _presentationBuffer[i] = 0xFF;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _presentationBuffer, width * 4, 0);
        }
    }

    private void RefreshReadings()
    {
        if (_source is not { } source)
        {
            return;
        }

        FpsText.Text = $"{source.FramesPerSecond:F0} FPS";
        FrameCountText.Text = $"累计 {source.FramesDelivered} 帧";
        ResolutionText.Text = source.FrameWidth > 0
            ? $"分辨率：{source.FrameWidth}×{source.FrameHeight}"
            : "分辨率：—";
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

    private void TearDownSource()
    {
        if (_source is not { } source)
        {
            return;
        }

        source.FrameArrived -= OnFrameArrived;
        source.Stopped -= OnSourceStopped;
        source.Stop();
        source.Dispose();
        _source = null;
        StopButton.IsEnabled = false;
        BackendText.Text = "后端：未启动";
        SourceText.Text = "目标：—";
    }

    private void SetStatus(string text) => StatusText.Text = $"状态：{text}";
}
