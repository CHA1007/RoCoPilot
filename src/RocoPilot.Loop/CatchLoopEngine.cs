using RocoPilot.Core;
using RocoPilot.Detection;
using RocoPilot.Input;

namespace RocoPilot.Loop;

public sealed class CatchLoopEngine : IDisposable
{
    private const int SleepChunkMs = 100;
    private const int VerifySettleMs = 80;

    private readonly CatchLoopOptions _options;
    private readonly CatchLoopMode _mode;
    private readonly ICenteringSensor _sensor;
    private readonly IInputDriver _driver;
    private readonly CenteringController _controller;
    private readonly CatchEventBus _bus;
    private readonly Random _random;
    private readonly Action<int, CancellationToken> _sleep;
    private readonly Func<long> _nowMs;
    private readonly Func<bool>? _inputGate;

    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private readonly object _stateGate = new();
    private CatchLoopState _state = CatchLoopState.Idle;
    private CatchPhase _phase = CatchPhase.Scanning;
    private int _pauseEntered;

    private long _lastSettleMs;
    private bool _stallAlerted;
    private int _seq;
    private int _activeTrackId = -1;

    public CatchLoopEngine(
        CatchLoopOptions options,
        CatchLoopMode mode,
        ICenteringSensor sensor,
        IInputDriver driver,
        CenteringController controller,
        CatchEventBus bus,
        Random? random = null,
        Action<int, CancellationToken>? sleep = null,
        Func<long>? nowMs = null,
        Func<bool>? inputGate = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Normalized();
        _mode = mode;
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _random = random ?? Random.Shared;
        _sleep = sleep ?? LoopTiming.Sleep;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _inputGate = inputGate;
        _controller.EventRaised += OnControllerEvent;
    }

    public CatchLoopState State
    {
        get { lock (_stateGate) { return _state; } }
    }

    public CatchPhase Phase
    {
        get { lock (_stateGate) { return _phase; } }
    }

    public CatchLoopMode Mode => _mode;

    public CatchEventBus Bus => _bus;

    public int ActiveTrackId => Volatile.Read(ref _activeTrackId);

    public bool Pause(string source = "manual")
    {
        lock (_stateGate)
        {
            if (_state != CatchLoopState.Running)
            {
                return false;
            }

            _state = CatchLoopState.Paused;
        }

        Interlocked.Exchange(ref _pauseEntered, 1);
        _pauseGate.Reset();
        _bus.Emit("paused", new Dictionary<string, object?> { ["source"] = source });
        return true;
    }

    public bool Resume(string source = "manual")
    {
        lock (_stateGate)
        {
            if (_state != CatchLoopState.Paused)
            {
                return false;
            }

            _state = CatchLoopState.Running;
        }

        _pauseGate.Set();
        _bus.Emit("resumed", new Dictionary<string, object?> { ["source"] = source });
        return true;
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (_state != CatchLoopState.Idle)
            {
                throw new LoopException($"单活跃循环：当前态 {_state}，仅 Idle 可开新会话");
            }

            _state = CatchLoopState.Running;
            _phase = CatchPhase.Scanning;
        }

        Interlocked.Exchange(ref _pauseEntered, 0);
        _pauseGate.Set();
        _seq = 0;
        _stallAlerted = false;
        _lastSettleMs = _nowMs();

        var stats = new SessionStats();
        var reason = "stop";
        try
        {
            EmitSessionStart();

            while (!cancellationToken.IsCancellationRequested && stats.Attempts < _options.MaxAttempts)
            {
                WaitAtLoopHead(cancellationToken);

                SetPhase(CatchPhase.Scanning);

                StableTarget? pick = null;
                var scanWaited = 0;
                var (cx, cy) = LoopGuards.ScreenCenter(_sensor);
                var centerX = (float)cx;
                var centerY = (float)cy;
                while (scanWaited < 1000)
                {
                    SleepInterruptible(LoopTiming.RestabilizePollMs, cancellationToken);
                    scanWaited += LoopTiming.RestabilizePollMs;
                    pick = TargetSelection.Pick(_sensor.ObserveStable(), lockedTrackId: null, centerX, centerY);
                    if (pick is not null) break;
                }
                if (pick is null)
                {
                    CheckStall();
                    SleepInterruptible(_controller.AppliedOptions.RecheckMs, cancellationToken);
                    continue;
                }

                stats.Attempts++;
                Volatile.Write(ref _activeTrackId, pick.TrackId);
                EmitTargetAcquired(stats.Attempts, pick, centerX, centerY);

                var aimY = pick.MedianCenter.Y + pick.Latest.Height * (float)_options.AimOffsetY;
                var offsetX = (double)pick.MedianCenter.X - centerX;
                var offsetY = (double)aimY - centerY;

                var tolerance = _controller.AppliedOptions.TolerancePx;
                var needTurn = Math.Abs(offsetX) > tolerance || Math.Abs(offsetY) > tolerance;

                double? ppc = null;
                if (needTurn)
                {
                    ppc = ResolvePpc(offsetX, offsetY);
                }

                WaitWhileGateClosed(cancellationToken);
                SetPhase(CatchPhase.Throwing);

                if (_mode == CatchLoopMode.DryRun)
                {
                    SleepInterruptible(_controller.AppliedOptions.RecheckMs, cancellationToken);
                    continue;
                }

                if (_mode == CatchLoopMode.Live)
                {
                    if (needTurn)
                    {
                        if (_options.AimJitterPx > 0)
                        {
                            offsetX += (_random.NextDouble() * 2 - 1) * _options.AimJitterPx;
                            offsetY += (_random.NextDouble() * 2 - 1) * _options.AimJitterPx;
                        }

                        TurnToward(offsetX, offsetY);
                        SleepInterruptible(_controller.AppliedOptions.RecheckMs, cancellationToken);
                    }

                    var chargeMs = _options.ChargeMs;
                    if (_options.ChargeJitterMs > 0)
                    {
                        chargeMs += _random.Next(-_options.ChargeJitterMs, _options.ChargeJitterMs + 1);
                    }

                    _driver.KeyDown(InputKey.LeftMouse);
                    try
                    {
                        SleepInterruptible(chargeMs, cancellationToken);
                    }
                    finally
                    {
                        _driver.KeyUp(InputKey.LeftMouse);
                    }
                }
                else if (_mode == CatchLoopMode.MoveOnly && needTurn)
                {
                    TurnToward(offsetX, offsetY);
                }

                var seq = ++_seq;
                stats.Throws++;
                _bus.Emit("throw_fired", new Dictionary<string, object?>
                {
                    ["seq"] = seq,
                    ["attempt"] = stats.Attempts,
                    ["offset_px"] = Math.Round(Math.Sqrt(offsetX * offsetX + offsetY * offsetY), 1),
                    ["ppc"] = ppc is { } p ? Math.Round(p, 3) : null,
                });

                SetPhase(CatchPhase.Settling);
                SleepInterruptible(_options.SettleMs, cancellationToken);
                var recheck = TargetSelection.Pick(
                    _sensor.ObserveStable(), pick.TrackId, pick.MedianCenter.X, pick.MedianCenter.Y);
                var present = recheck is not null;
                _bus.Emit("settled", new Dictionary<string, object?>
                {
                    ["seq"] = seq,
                    ["result"] = present ? "present" : "gone",
                });
                if (present)
                {
                    stats.Present++;
                }
                else
                {
                    stats.Gone++;
                    _lastSettleMs = _nowMs();
                    _stallAlerted = false;
                }

                if (needTurn && ppc is { } usedPpc && usedPpc > 0)
                {
                    TryOnlineCalibration(offsetX, offsetY, pick, recheck, usedPpc);
                }

                Volatile.Write(ref _activeTrackId, -1);
                var postSettleDelay = NextPostSettleDelayMs();
                SleepInterruptible(postSettleDelay, cancellationToken);
                _bus.Counters.AddRest(postSettleDelay);
            }

            reason = stats.Attempts >= _options.MaxAttempts ? "max_attempts" : "stop";
        }
        catch (OperationCanceledException)
        {
            reason = "stop";
        }
        catch (Exception ex)
        {
            reason = "fault";
            SafeEmitFault(ex);
            throw;
        }
        finally
        {
            EmitSessionStop(reason, stats);
            lock (_stateGate)
            {
                _state = CatchLoopState.Idle;
                _phase = CatchPhase.Scanning;
            }
        }
    }

    public void Dispose()
    {
        _controller.EventRaised -= OnControllerEvent;
        _pauseGate.Dispose();
    }

    private double? ResolvePpc(double offsetX, double offsetY)
    {
        var magnitude = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        var cached = _controller.Cache.PpcFor(magnitude);
        if (cached is { } ppc)
        {
            return ppc;
        }

        return _controller.AppliedOptions.FallbackDivisor;
    }

    private void TurnToward(double offsetX, double offsetY)
    {
        var fallback = _controller.AppliedOptions.FallbackDivisor;
        var ppcXVal = _options.PpcX > 0 ? _options.PpcX : fallback;
        var ppcYVal = _options.PpcY > 0 ? _options.PpcY : fallback;
        var maxCounts = _controller.AppliedOptions.MaxStepCounts;
        var countsX = Math.Clamp((int)Math.Round(offsetX / ppcXVal), -maxCounts, maxCounts);
        var countsY = Math.Clamp((int)Math.Round(offsetY / ppcYVal), -maxCounts, maxCounts);
        if (_options.CommandNoiseCounts > 0)
        {
            countsX += (int)Math.Round((_random.NextDouble() * 2 - 1) * _options.CommandNoiseCounts);
            countsY += (int)Math.Round((_random.NextDouble() * 2 - 1) * _options.CommandNoiseCounts);
        }

        _driver.MoveRelative(countsX, countsY);
    }

    private void TryOnlineCalibration(
        double offsetX, double offsetY,
        StableTarget before, StableTarget? after, double usedPpc)
    {
        if (after is null)
        {
            return;
        }

        var movedX = (double)after.MedianCenter.X - before.MedianCenter.X;
        var movedY = (double)after.MedianCenter.Y - before.MedianCenter.Y;

        var useX = Math.Abs(offsetX) >= Math.Abs(offsetY);
        var commandCounts = useX ? offsetX / usedPpc : offsetY / usedPpc;
        var movedPx = useX ? movedX : movedY;

        var minCommand = _controller.AppliedOptions.OnlineMinCommandCounts;
        var minMoved = _controller.AppliedOptions.OnlineMinMovedPx;
        if (Math.Abs(commandCounts) < minCommand || Math.Abs(movedPx) < minMoved)
        {
            return;
        }

        if (movedPx * commandCounts >= 0)
        {
            return;
        }

        var observedPpc = Math.Clamp(
            Math.Abs(movedPx) / Math.Abs(commandCounts),
            _controller.AppliedOptions.MinPpc,
            _controller.AppliedOptions.MaxPpc);

        var result = _controller.Cache.ApplyOnlineObservation(
            commandCounts, observedPpc,
            _controller.AppliedOptions.OnlineEmaWeight,
            _controller.AppliedOptions.OnlineRelativeChangeThreshold);

        if (result.Seeded || result.Significant)
        {
            _bus.Emit("calibration", new Dictionary<string, object?>
            {
                ["source"] = "online",
                ["ppc"] = Math.Round(result.PixelsPerCount, 3),
            });
        }
    }

    private void OnControllerEvent(object? sender, ToolEvent toolEvent) => _bus.Forward(toolEvent);

    private bool WaitAtLoopHead(CancellationToken cancellationToken)
    {
        while (true)
        {
            _pauseGate.Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_inputGate?.Invoke() is not false)
            {
                break;
            }

            _sleep(SleepChunkMs, cancellationToken);
        }

        return Interlocked.Exchange(ref _pauseEntered, 0) == 1;
    }

    private void WaitWhileGateClosed(CancellationToken cancellationToken)
    {
        while (true)
        {
            _pauseGate.Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_inputGate?.Invoke() is not false)
            {
                return;
            }

            _sleep(SleepChunkMs, cancellationToken);
        }
    }

    private void SleepInterruptible(int milliseconds, CancellationToken cancellationToken)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            _pauseGate.Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Math.Min(remaining, SleepChunkMs);
            _sleep(chunk, cancellationToken);
            remaining -= chunk;
        }
    }

    private void CheckStall()
    {
        var sinceMs = _nowMs() - _lastSettleMs;
        if (sinceMs <= _options.StallAlertMs || _stallAlerted)
        {
            return;
        }

        _stallAlerted = true;
        _bus.Emit("stall_alert", new Dictionary<string, object?> { ["since_settle_s"] = (int)(sinceMs / 1000) });
    }

    private void EmitSessionStart()
    {
        var (width, height) = _sensor.LatestFrameSize;
        _bus.Emit("session_start", new Dictionary<string, object?>
        {
            ["mode"] = ModeString(_mode),
            ["frame_size"] = new[] { width, height },
            ["input_backend"] = _driver.BackendName,
            ["tolerance_px"] = _controller.AppliedOptions.TolerancePx,
        });
    }

    private void EmitSessionStop(string reason, SessionStats stats)
    {
        _bus.Emit("session_stop", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["attempts"] = stats.Attempts,
            ["throws"] = stats.Throws,
            ["present"] = stats.Present,
            ["gone"] = stats.Gone,
        });
    }

    private void EmitTargetAcquired(int attempt, StableTarget pick, double centerX, double centerY)
    {
        var offsetX = (double)pick.MedianCenter.X - centerX;
        var offsetY = (double)pick.MedianCenter.Y - centerY;
        _bus.Emit("target_acquired", new Dictionary<string, object?>
        {
            ["attempt"] = attempt,
            ["cls_name"] = pick.Latest.ClassName,
            ["conf"] = Math.Round(pick.Latest.Confidence, 3),
            ["area"] = (int)Math.Round(pick.Latest.Area),
            ["offset_px"] = Math.Round(Math.Sqrt(offsetX * offsetX + offsetY * offsetY), 1),
        });
    }

    private void SafeEmitFault(Exception exception)
    {
        try
        {
            _bus.Emit("fault", new Dictionary<string, object?>
            {
                ["error"] = exception.GetBaseException().Message,
            });
        }
        catch
        {
        }
    }

    private void SetPhase(CatchPhase phase)
    {
        lock (_stateGate)
        {
            _phase = phase;
        }
    }

    private int NextPostSettleDelayMs() =>
        _options.PostSettleDelayMinMs == _options.PostSettleDelayMaxMs
            ? _options.PostSettleDelayMinMs
            : _random.Next(_options.PostSettleDelayMinMs, _options.PostSettleDelayMaxMs + 1);

    private static string ModeString(CatchLoopMode mode) => mode switch
    {
        CatchLoopMode.DryRun => "dry",
        CatchLoopMode.MoveOnly => "move",
        _ => "live",
    };

    private sealed class SessionStats
    {
        public int Attempts;
        public int Throws;
        public int Present;
        public int Gone;
    }
}
