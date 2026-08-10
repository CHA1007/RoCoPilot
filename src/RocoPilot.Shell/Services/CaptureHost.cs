using RocoPilot.Capture;

namespace RocoPilot.Shell.Services;

public sealed class CaptureHost : IDisposable
{
    public const string DispatcherConsumer = "Dispatcher";

    private readonly object _gate = new();
    private readonly HashSet<string> _owners = new();
    private ICaptureSource? _source;
    private Task? _pendingStart;

    public string WindowTitleSubstring { get; set; } = string.Empty;

    public CaptureBackendMode Backend { get; set; } = CaptureBackendMode.Auto;

    public bool IsRunning
    {
        get { lock (_gate) { return _source is not null; } }
    }

    public ICaptureSource? CurrentSource
    {
        get { lock (_gate) { return _source; } }
    }

    public bool HasOwners
    {
        get { lock (_gate) { return _owners.Count > 0; } }
    }

    public event Action? Changed;

    public event Action<string>? AutoStartFailed;

    public IDisposable Acquire(string owner)
    {
        bool shouldStart;
        lock (_gate)
        {
            var added = _owners.Add(owner);
            shouldStart = added && _owners.Count == 1;
        }

        if (shouldStart)
        {
            _ = EnsureStartAsync();
        }

        return new CaptureLease(this, owner);
    }

    public void RetryStart()
    {
        if (HasOwners)
        {
            _ = EnsureStartAsync();
        }
    }

    public Task EnsureStartAsync()
    {
        lock (_gate)
        {
            if (_source is not null || _pendingStart is not null)
            {
                return _pendingStart ?? Task.CompletedTask;
            }

            _pendingStart = StartCoreAsync();
        }

        return _pendingStart!;
    }

    internal void Release(string owner)
    {
        bool shouldStop;
        lock (_gate)
        {
            _owners.Remove(owner);
            shouldStop = _owners.Count == 0;
        }

        if (shouldStop)
        {
            Stop();
        }
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var title = ResolveWindowTitle();
            if (string.IsNullOrWhiteSpace(title))
            {
                AutoStartFailed?.Invoke("未检测到已启动的游戏窗口，请先启动游戏");
                return;
            }

            await StartAsync(title, Backend);
        }
        finally
        {
            lock (_gate) { _pendingStart = null; }
        }
    }

    private string ResolveWindowTitle()
    {
        var hwnd = WindowFinder.FindByProcessName(WindowFinder.GameProcessName);
        if (hwnd != IntPtr.Zero && WindowFinder.GetTitle(hwnd) is { Length: > 0 } title)
        {
            WindowTitleSubstring = title;
            return title;
        }

        return WindowTitleSubstring;
    }

    public async Task<bool> StartAsync(string windowTitle, CaptureBackendMode backend = CaptureBackendMode.Auto)
    {
        lock (_gate)
        {
            if (_source is not null) return true;
        }

        try
        {
            var options = new CaptureOptions { WindowTitleSubstring = windowTitle, Backend = backend };
            var source = await CaptureSourceFactory.StartBestAvailableAsync(options);
            lock (_gate)
            {
                if (_source is not null)
                {
                    source.Stop();
                    source.Dispose();
                    return true;
                }

                source.Stopped += (_, _) =>
                {
                    lock (_gate) { _source = null; }
                    Changed?.Invoke();
                };
                _source = source;
            }

            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        ICaptureSource? source;
        lock (_gate)
        {
            source = _source;
            _source = null;
        }

        if (source is null) return;
        source.Stop();
        source.Dispose();
        Changed?.Invoke();
    }

    public void Dispose() => Stop();

    private sealed class CaptureLease : IDisposable
    {
        private readonly CaptureHost _host;
        private readonly string _owner;
        private bool _disposed;

        public CaptureLease(CaptureHost host, string owner)
        {
            _host = host;
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.Release(_owner);
        }
    }
}