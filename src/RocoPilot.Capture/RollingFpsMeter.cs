using System.Diagnostics;

namespace RocoPilot.Capture;

public sealed class RollingFpsMeter
{
    private readonly TimeSpan _window;
    private readonly Func<long> _now;
    private readonly Queue<long> _timestamps = new();
    private readonly object _gate = new();

    public RollingFpsMeter(TimeSpan? window = null, Func<long>? timestampSource = null)
    {
        _window = window ?? TimeSpan.FromSeconds(10);
        _now = timestampSource ?? Stopwatch.GetTimestamp;
    }

    public void Tick()
    {
        var now = _now();
        var cutoff = now - WindowTicks;
        lock (_gate)
        {
            _timestamps.Enqueue(now);
            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
            {
                _timestamps.Dequeue();
            }
        }
    }

    public double CurrentFps
    {
        get
        {
            long first, last;
            int count;
            var cutoff = _now() - WindowTicks;
            lock (_gate)
            {
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                {
                    _timestamps.Dequeue();
                }

                count = _timestamps.Count;
                if (count < 2)
                {
                    return 0.0;
                }

                first = _timestamps.Peek();
                last = LastEnqueued();
            }

            var spanTicks = last - first;
            return spanTicks <= 0 ? 0.0 : (count - 1) * ((double)Stopwatch.Frequency / spanTicks);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _timestamps.Clear();
        }
    }

    private long WindowTicks => (long)(_window.TotalSeconds * Stopwatch.Frequency);

    private long LastEnqueued()
    {
        long last = 0;
        foreach (var t in _timestamps)
        {
            last = t;
        }

        return last;
    }
}
