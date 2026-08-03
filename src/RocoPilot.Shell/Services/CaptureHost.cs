using RocoPilot.Capture;

namespace RocoPilot.Shell.Services;

public sealed class CaptureHost : IDisposable
{
    private readonly object _gate = new();
    private ICaptureSource? _source;

    public bool IsRunning
    {
        get { lock (_gate) { return _source is not null; } }
    }

    public ICaptureSource? CurrentSource
    {
        get { lock (_gate) { return _source; } }
    }

    public event Action? Changed;

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
}
