using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Loop;

namespace RocoPilot.Shell.Overlay;

public sealed class OverlayController
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DebugTickInterval = TimeSpan.FromMilliseconds(33);

    private const double EdgeMargin = 12;

    private readonly RunningTaskHost _host;
    private readonly CaptureHost _capture;
    private readonly ISettingsStore _store;
    private readonly object _gate = new();

    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private OverlayWindow? _window;
    private DebugOverlayWindow? _debugWindow;
    private DispatcherTimer? _debugTimer;
    private OverlayProjection _projection = new();
    private IRunningTask? _observed;

    public OverlayController(RunningTaskHost host, CaptureHost capture, ISettingsStore store)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _host.Changed += OnHostChanged;
        _capture.Changed += OnCaptureChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
        _timer.Tick += (_, _) => TrackAndRender();
        _timer.Start();
        _debugTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebugTickInterval };
        _debugTimer.Tick += (_, _) => RenderDebugOverlay();
        _debugTimer.Start();
        SyncTask();
    }

    public void Shutdown()
    {
        _host.Changed -= OnHostChanged;
        _capture.Changed -= OnCaptureChanged;
        _timer?.Stop();
        _timer = null;
        _debugTimer?.Stop();
        _debugTimer = null;
        Observe(null);
        _debugWindow?.Close();
        _debugWindow = null;
        _window?.Close();
        _window = null;
    }

    private void OnHostChanged()
    {
        var dispatcher = _dispatcher;
        dispatcher?.InvokeAsync(SyncTask);
    }

    private void OnCaptureChanged()
    {
        var dispatcher = _dispatcher;
        dispatcher?.InvokeAsync(() =>
        {
            lock (_gate)
            {
                _projection.ApplyCapture(_capture.IsRunning);
            }

            TrackAndRender();
        });
    }

    private void SyncTask()
    {
        var current = _host.Current;
        if (!ReferenceEquals(current, _observed))
        {
            if (current is not null)
            {
                lock (_gate)
                {
                    _projection = new OverlayProjection();
                    _projection.ApplyCapture(_capture.IsRunning);
                }
            }

            Observe(current);
        }

        TrackAndRender();
    }

    private void Observe(IRunningTask? task)
    {
        if (_observed is not null)
        {
            _observed.StateChanged -= OnTaskStateChanged;
            _observed.EventRaised -= OnToolEvent;
        }

        _observed = task;
        if (task is not null)
        {
            task.StateChanged += OnTaskStateChanged;
            task.EventRaised += OnToolEvent;
        }
    }

    private void OnTaskStateChanged(object? sender, TaskState state)
    {
        OverlayProjection projection;
        lock (_gate)
        {
            projection = _projection;
        }

        projection.ApplyState(state);
        RenderNow();
    }

    private void OnToolEvent(object? sender, ToolEvent toolEvent)
    {
        OverlayProjection projection;
        lock (_gate)
        {
            projection = _projection;
        }

        projection.ApplyEvent(toolEvent);
        RenderNow();
    }

    private void RenderNow()
    {
        var dispatcher = _dispatcher;
        dispatcher?.InvokeAsync(() =>
        {
            if (_window?.IsVisible == true)
            {
                _window.Apply(TakeSnapshot());
            }
        });
    }

    private void TrackAndRender()
    {
        var snapshot = TakeSnapshot();

        var hasBusiness = snapshot.CaptureRunning;
        var game = hasBusiness ? WindowFinder.FindByProcessName(WindowFinder.GameProcessName) : IntPtr.Zero;
        if (game == IntPtr.Zero || OverlayNative.IsIconic(game) || !WindowFinder.IsForegroundProcess(WindowFinder.GameProcessName))
        {
            if (_window?.IsVisible == true)
            {
                _window.Hide();
            }

            return;
        }

        EnsureWindow();
        if (!OverlayNative.GetWindowRect(game, out var rect))
        {
            return;
        }

        PlaceOver(rect);
        if (_window!.IsVisible)
        {
            _window.Apply(snapshot);
        }
        else
        {
            _window.Show();
            _window.Apply(snapshot);
        }
    }

    private OverlaySnapshot TakeSnapshot()
    {
        lock (_gate)
        {
            return _projection.Snapshot();
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new OverlayWindow();
        new WindowInteropHelper(_window).EnsureHandle();
    }

    private void PlaceOver(OverlayNative.RECT rect)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_window!).EnsureHandle());
        var scale = source?.CompositionTarget?.TransformFromDevice;
        var sx = scale?.M11 ?? 1.0;
        var sy = scale?.M22 ?? 1.0;

        var left = rect.Left * sx + EdgeMargin;
        var top = rect.Top * sy + EdgeMargin;
        if (double.IsNaN(_window!.Left) || Math.Abs(_window.Left - left) > 0.5)
        {
            _window.Left = left;
        }

        if (double.IsNaN(_window.Top) || Math.Abs(_window.Top - top) > 0.5)
        {
            _window.Top = top;
        }

        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        OverlayNative.SetWindowPos(
            hwnd, OverlayNative.HwndTopmost, 0, 0, 0, 0,
            OverlayNative.SwpNoMove | OverlayNative.SwpNoSize | OverlayNative.SwpNoActivate);
    }

    // ── 调试叠层（票 13 后续：检测框可视化） ──

    private void RenderDebugOverlay()
    {
        var enabled = _store.GetShellSettings().DebugOverlay;
        if (!enabled)
        {
            if (_debugWindow?.IsVisible == true) _debugWindow.Hide();
            return;
        }

        var pipeline = _host.Current?.DiagnosticsContext as ICatchPipeline;
        if (pipeline is null)
        {
            if (_debugWindow?.IsVisible == true) _debugWindow.Hide();
            return;
        }

        var game = pipeline.GameWindow;
        if (game == IntPtr.Zero || OverlayNative.IsIconic(game) || !WindowFinder.IsForegroundProcess(WindowFinder.GameProcessName))
        {
            if (_debugWindow?.IsVisible == true) _debugWindow.Hide();
            return;
        }

        if (!OverlayNative.GetWindowRect(game, out var rect))
        {
            return;
        }

        EnsureDebugWindow();
        PlaceDebugOver(rect);

        var targets = pipeline.ObserveDetections();
        var frameSize = pipeline.SensorFrameSize;
        var activeTrack = pipeline.ActiveTrackId;
        _debugWindow!.Render(targets, frameSize.Width, frameSize.Height, activeTrack);

        if (!_debugWindow.IsVisible)
        {
            _debugWindow.Show();
        }

        var hwnd = new WindowInteropHelper(_debugWindow).EnsureHandle();
        OverlayNative.SetWindowPos(
            hwnd, OverlayNative.HwndTopmost, 0, 0, 0, 0,
            OverlayNative.SwpNoMove | OverlayNative.SwpNoSize | OverlayNative.SwpNoActivate);
    }

    private void EnsureDebugWindow()
    {
        if (_debugWindow is not null) return;
        _debugWindow = new DebugOverlayWindow();
        new WindowInteropHelper(_debugWindow).EnsureHandle();
    }

    private void PlaceDebugOver(OverlayNative.RECT rect)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_debugWindow!).EnsureHandle());
        var scale = source?.CompositionTarget?.TransformFromDevice;
        var sx = scale?.M11 ?? 1.0;
        var sy = scale?.M22 ?? 1.0;

        var left = rect.Left * sx;
        var top = rect.Top * sy;
        var width = (rect.Right - rect.Left) * sx;
        var height = (rect.Bottom - rect.Top) * sy;

        if (double.IsNaN(_debugWindow!.Left) || Math.Abs(_debugWindow.Left - left) > 0.5)
            _debugWindow.Left = left;
        if (double.IsNaN(_debugWindow.Top) || Math.Abs(_debugWindow.Top - top) > 0.5)
            _debugWindow.Top = top;
        if (Math.Abs(_debugWindow.Width - width) > 0.5)
            _debugWindow.Width = width;
        if (Math.Abs(_debugWindow.Height - height) > 0.5)
            _debugWindow.Height = height;
    }
}
