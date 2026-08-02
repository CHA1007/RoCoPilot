using RocoPilot.Core;

namespace RocoPilot.Loop;

public sealed record CatchCountersSnapshot(
    CatchLoopState State,
    int Throws,
    int Settled,
    double ThrowsPerHour,
    double? CenteringRate,
    TimeSpan RunDuration,
    TimeSpan RestDuration,
    TimeSpan SinceLastSettle);

public sealed class CatchCounters
{
    private readonly Func<long> _nowMs;
    private readonly long _rateWindowMs;
    private readonly object _gate = new();
    private readonly Queue<long> _throwTicks = new();

    private CatchLoopState _state = CatchLoopState.Idle;
    private int _acquired;
    private int _centered;
    private int _throws;
    private int _settled;
    private long _runAccumMs;
    private long _runStartMs;
    private long _lastSettleMs;
    private long _restMs;
    private bool _hasSession;

    public CatchCounters(Func<long>? nowMs = null, int rateWindowMinutes = 10)
    {
        if (rateWindowMinutes <= 0)
        {
            throw new LoopException($"率窗口须为正分钟，实得 {rateWindowMinutes}");
        }

        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _rateWindowMs = rateWindowMinutes * 60_000L;
    }

    public void Record(ToolEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);
        lock (_gate)
        {
            var now = _nowMs();
            switch (toolEvent.Name)
            {
                case "session_start":
                    _state = CatchLoopState.Running;
                    _acquired = _centered = _throws = _settled = 0;
                    _throwTicks.Clear();
                    _runAccumMs = 0;
                    _runStartMs = now;
                    _lastSettleMs = now;
                    _hasSession = true;
                    break;
                case "paused":
                    if (_state == CatchLoopState.Running)
                    {
                        _runAccumMs += now - _runStartMs;
                        _state = CatchLoopState.Paused;
                    }

                    break;
                case "resumed":
                    if (_state == CatchLoopState.Paused)
                    {
                        _runStartMs = now;
                        _state = CatchLoopState.Running;
                    }

                    break;
                case "session_stop":
                    if (_state == CatchLoopState.Running)
                    {
                        _runAccumMs += now - _runStartMs;
                    }

                    _state = CatchLoopState.Idle;
                    break;
                case "target_acquired":
                    _acquired++;
                    break;
                case "centered":
                    _centered++;
                    break;
                case "throw_fired":
                    _throws++;
                    _throwTicks.Enqueue(now);
                    break;
                case "settled":
                    if (string.Equals(toolEvent.Data?.GetValueOrDefault("result") as string, "gone", StringComparison.Ordinal))
                    {
                        _settled++;
                        _lastSettleMs = now;
                    }

                    break;
            }
        }
    }

    internal void AddRest(int milliseconds)
    {
        lock (_gate)
        {
            _restMs += milliseconds;
        }
    }

    public CatchCountersSnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = _nowMs();
            while (_throwTicks.Count > 0 && now - _throwTicks.Peek() > _rateWindowMs)
            {
                _throwTicks.Dequeue();
            }

            var runMs = _runAccumMs + (_state == CatchLoopState.Running ? now - _runStartMs : 0);
            var throwsPerHour = _throwTicks.Count * (3_600_000.0 / _rateWindowMs);
            double? centeringRate = _acquired == 0 ? null : _centered / (double)_acquired;
            var sinceSettle = _hasSession ? now - _lastSettleMs : 0;

            return new CatchCountersSnapshot(
                _state,
                _throws,
                _settled,
                throwsPerHour,
                centeringRate,
                TimeSpan.FromMilliseconds(Math.Max(0, runMs)),
                TimeSpan.FromMilliseconds(_restMs),
                TimeSpan.FromMilliseconds(Math.Max(0, sinceSettle)));
        }
    }
}
