using System.Diagnostics;

namespace RocoPilot.Handpan;

public sealed class PlaybackClock
{
    private readonly Func<double> _now;
    private double _accumulated;
    private double? _runStartedAt;

    public PlaybackClock(Func<double>? now = null) => _now = now ?? StopwatchReader();

    public bool IsRunning => _runStartedAt is not null;

    public double Elapsed => _runStartedAt is { } startedAt ? _accumulated + (_now() - startedAt) : _accumulated;

    public void Start()
    {
        _accumulated = 0;
        _runStartedAt = _now();
    }

    public void Pause()
    {
        if (_runStartedAt is { } startedAt)
        {
            _accumulated += _now() - startedAt;
            _runStartedAt = null;
        }
    }

    public void Resume() => _runStartedAt ??= _now();

    private static Func<double> StopwatchReader()
    {
        var stopwatch = Stopwatch.StartNew();
        return () => stopwatch.Elapsed.TotalSeconds;
    }
}
