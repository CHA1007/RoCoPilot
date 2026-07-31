using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Overlay;

public sealed class OverlayController
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    private const double EdgeMargin = 12;

    private readonly RunningTaskHost _host;
    private readonly CaptureHost _capture;
    private readonly object _gate = new();

    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private OverlayWindow? _window;
    private OverlayProjection _projection = new();
    private IRunningTask? _observed;
    private string _windowTitle = AutoThrowSettings.DefaultWindowTitle;

    public OverlayController(RunningTaskHost host, CaptureHost capture)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _host.Changed += OnHostChanged;
        _capture.Changed += OnCaptureChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
        _timer.Tick += (_, _) => TrackAndRender();
        _timer.Start();
        SyncTask();
    }

    public void Shutdown()
    {
        _host.Changed -= OnHostChanged;
        _capture.Changed -= OnCaptureChanged;
        _timer?.Stop();
        _timer = null;
        Observe(null);
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
        var launch = _host.Active;
        if (launch?.Settings is AutoThrowSettings settings &&
            !string.IsNullOrWhiteSpace(settings.WindowTitleSubstring))
        {
            _windowTitle = settings.WindowTitleSubstring;
        }

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

        var hasBusiness = _observed is not null || snapshot.FailureLine is not null || snapshot.CaptureRunning;
        var game = hasBusiness ? WindowFinder.FindFirstByTitleSubstring(_windowTitle) : IntPtr.Zero;
        if (game == IntPtr.Zero || OverlayNative.IsIconic(game) || WindowFinder.GetForegroundWindow() != game)
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
}
