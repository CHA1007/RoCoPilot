using System.IO;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Handpan;
using RocoPilot.Input;

namespace RocoPilot.Tools.Handpan;

public sealed class HandpanRunningTask : RunningTaskBase
{
    private const double LoopIntervalSeconds = 1.5;
    private const double ProgressIntervalSeconds = 0.1;
    private const int FocusPollMs = 250;
    private const int WaitSliceMs = 1;
    private const int CoarseWaitSliceMs = 8;
    private const double CoarseWaitFloorSeconds = 0.02;

    private readonly HandpanSettings _settings;
    private readonly Func<IInputDriver> _driverFactory;
    private readonly Func<bool> _gameFocused;
    private readonly Func<bool> _activateGameWindow;
    private readonly Func<(int X, int Y)?> _wakePoint;
    private readonly HandpanScoreStore _scores;
    private readonly PauseGate _pauseGate = new();
    private readonly object _heldGate = new();
    private readonly HashSet<string> _heldKeys = [];

    private IInputDriver? _driver;
    private Thread? _focusWatcher;
    private HandpanArrangement? _arrangement;
    private double _progressTotalSeconds;
    private double _lastProgressSeconds;
    private int _round;

    public HandpanRunningTask(
        HandpanSettings settings,
        Func<IInputDriver>? driverFactory = null,
        Func<bool>? gameFocused = null,
        Func<bool>? activateGameWindow = null,
        HandpanScoreStore? scores = null,
        Func<(int X, int Y)?>? wakePoint = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _driverFactory = driverFactory ?? InputDriverFactory.Create;
        _gameFocused = gameFocused ?? (() => WindowFinder.IsForegroundProcess(WindowFinder.GameProcessName));
        _activateGameWindow = activateGameWindow ?? WindowFinder.ActivateGameWindow;
        _scores = scores ?? new HandpanScoreStore();
        _wakePoint = wakePoint ?? WindowFinder.GetGameClickPoint;
    }

    public override string ToolId => HandpanTool.ToolId;

    public override object? DiagnosticsContext => _arrangement;

    public event Action<HandpanProgress>? ProgressChanged;

    public override void RequestPause(string source = "manual")
    {
        _pauseGate.HoldManually();
        SyncState(PauseSource.Manual);
    }

    public override void RequestResume(string source = "manual")
    {
        _pauseGate.ReleaseManually();
        SyncState(PauseSource.Manual);
    }

    protected override async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            _settings.SanitizeInPlace();
            if (!await Arming.ExecuteAsync(ArmingSteps(), RaiseEvent, cancellationToken))
            {
                return;
            }

            if (!TryEnterRunning())
            {
                return;
            }

            RaiseStateChanged(TaskState.Running);
            StartFocusWatcher(cancellationToken);
            if (_settings.Mode == HandpanMode.Probe)
            {
                await Task.Run(() => Probe(cancellationToken), cancellationToken);
            }
            else
            {
                await Task.Run(() => Perform(_arrangement!.Plan, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Raise(new HandpanFaulted(ex.GetBaseException().Message));
        }
        finally
        {
            StopFocusWatcher();
            ReleaseHeldKeys();
            DisposeDriver();
            FinishStopped();
        }
    }

    protected override void DisposeCore()
    {
        _pauseGate.Dispose();
        DisposeDriver();
    }

    private IReadOnlyList<ArmingStep> ArmingSteps()
    {
        List<ArmingStep> steps = [ActivateGameWindowStep(), MountDriverStep()];
        if (_settings.Mode != HandpanMode.Probe)
        {
            steps.Add(ArrangeScoreStep());
        }

        steps.Add(WakeGameStep());
        return steps;
    }

    private ArmingStep ActivateGameWindowStep() =>
        new("激活游戏窗口", "把《洛克王国：世界》切到前台", cancellationToken =>
        {
            if (!_activateGameWindow())
            {
                throw new InvalidOperationException("游戏窗口没有被真正激活");
            }

            return Task.CompletedTask;
        })
        {
            Remedy = _ => "确认游戏未最小化；手动点一下游戏窗口后重试，若游戏以管理员运行则本程序也须以管理员运行",
        };

    private ArmingStep WakeGameStep() =>
        new("点击唤醒游戏", "在游戏画面上点一下，让游戏接管键盘输入", cancellationToken =>
        {
            if (_wakePoint() is not { } point)
            {
                throw new InvalidOperationException("游戏画面上找不到没被遮住的落点");
            }

            var driver = _driver ?? throw new InvalidOperationException("输入驱动未挂载");
            driver.ClickAt(point.X, point.Y);
            if (!_gameFocused())
            {
                throw new InvalidOperationException("点完之后游戏窗口没回到前台");
            }

            return Task.CompletedTask;
        })
        {
            Remedy = _ => "把本程序窗口移开或最小化，别遮住游戏画面；游戏窗口也不能最小化",
        };

    private ArmingStep MountDriverStep() =>
        new("挂载输入驱动", "挂载 Interception 键盘驱动", cancellationToken =>
        {
            var driver = _driverFactory();
            driver.Arm();
            _driver = driver;
            return Task.CompletedTask;
        })
        {
            Remedy = _ => "以管理员身份运行安装器安装 Interception 驱动后重试",
        };

    private ArmingStep ArrangeScoreStep() =>
        new("扒谱并生成演奏计划", "解析 MIDI 并适配到手碟音域", cancellationToken =>
        {
            _arrangement = Arrange();
            return Task.CompletedTask;
        })
        {
            Remedy = _ => "确认曲谱已导入且未损坏，或换一个曲谱",
        };

    private HandpanArrangement Arrange()
    {
        var path = _scores.Resolve(_settings.ScoreName)
            ?? throw new FileNotFoundException($"曲谱库里没有「{_settings.ScoreName}」", _settings.ScoreName);

        var score = MidiParser.Parse(File.ReadAllBytes(path));
        return HandpanArranger.Arrange(
            score,
            _settings.ToKeyMap(),
            _settings.ScoreName,
            _settings.ToArrangement());
    }

    private void PublishArrangement()
    {
        var arrangement = _arrangement!;
        Raise(new HandpanArranged(
            arrangement.Plan.NoteCount,
            arrangement.Chart.MissingCount,
            arrangement.Fit.Semitones,
            arrangement.Fit.ExactCoverage,
            arrangement.Chart.TotalSeconds));
    }

    private void Probe(CancellationToken cancellationToken)
    {
        var probeKeys = ProbePlanner.Plan(_settings.ToKeyMap());
        if (probeKeys.Count == 0)
        {
            Raise(new HandpanFaulted("键位表没有可用条目", "检查键位表的音名与按键写法后重试"));
            return;
        }

        var clock = new PlaybackClock();
        clock.Start();
        foreach (var probeKey in probeKeys)
        {
            WaitUntil(clock, probeKey.DownAtSeconds, cancellationToken);
            Raise(new HandpanProbeKey(probeKey.Key, probeKey.Note));
            Send(new KeyEvent(probeKey.Key, probeKey.DownAtSeconds, IsDown: true));
            WaitUntil(clock, probeKey.UpAtSeconds, cancellationToken);
            Send(new KeyEvent(probeKey.Key, probeKey.UpAtSeconds, IsDown: false));
        }

        Raise(new HandpanProbeCompleted(probeKeys.Count));
    }

    private void Perform(PlaybackPlan plan, CancellationToken cancellationToken)
    {
        if (plan.KeyEvents.Count == 0)
        {
            Raise(new HandpanFaulted("没有可演奏的音符", "换一个 MIDI 文件，或检查键位表是否覆盖了旋律音域"));
            return;
        }

        Countdown(cancellationToken);
        PublishArrangement();
        var round = 0;
        var clock = new PlaybackClock();
        _progressTotalSeconds = plan.EndsAtSeconds;
        while (true)
        {
            clock.Start();
            _lastProgressSeconds = double.NegativeInfinity;
            foreach (var keyEvent in plan.KeyEvents)
            {
                WaitUntil(clock, keyEvent.AtSeconds, cancellationToken);
                Send(keyEvent);
            }

            PublishRoundEnd();
            if (!_settings.Loop)
            {
                break;
            }

            round++;
            _round = round;
            Raise(new HandpanRoundCompleted(round));
            WaitUntil(clock, clock.Elapsed + LoopIntervalSeconds, cancellationToken);
        }

        Raise(new HandpanCompleted());
    }

    private void Countdown(CancellationToken cancellationToken)
    {
        var seconds = _settings.CountdownSeconds;
        if (seconds <= 0)
        {
            return;
        }

        var clock = new PlaybackClock();
        clock.Start();
        for (var left = seconds; left > 0; left--)
        {
            Raise(new HandpanCountdown(left));
            WaitUntil(clock, seconds - left + 1, cancellationToken);
        }
    }

    private void WaitUntil(PlaybackClock clock, double atSeconds, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_pauseGate.IsOpen)
            {
                ReleaseHeldKeys();
                clock.Pause();
                _pauseGate.WaitUntilOpen(cancellationToken);
                clock.Resume();
                continue;
            }

            var remaining = atSeconds - clock.Elapsed;
            if (remaining <= 0)
            {
                return;
            }

            PublishProgress(clock);
            Thread.Sleep(remaining > CoarseWaitFloorSeconds
                ? Math.Min(CoarseWaitSliceMs, (int)((remaining - CoarseWaitFloorSeconds) * 1000))
                : WaitSliceMs);
        }
    }

    private void PublishProgress(PlaybackClock clock)
    {
        if (_progressTotalSeconds <= 0)
        {
            return;
        }

        var elapsed = clock.Elapsed;
        if (elapsed - _lastProgressSeconds < ProgressIntervalSeconds)
        {
            return;
        }

        _lastProgressSeconds = elapsed;
        PublishProgressAt(elapsed);
    }

    private void PublishRoundEnd()
    {
        _lastProgressSeconds = _progressTotalSeconds;
        PublishProgressAt(_progressTotalSeconds);
    }

    private void PublishProgressAt(double elapsedSeconds) =>
        ProgressChanged?.Invoke(new HandpanProgress(
            Math.Clamp(elapsedSeconds, 0, _progressTotalSeconds),
            _progressTotalSeconds,
            _round));

    private void Send(KeyEvent keyEvent)
    {
        var driver = _driver ?? throw new InvalidOperationException("输入驱动未挂载");
        var key = InputKey.Parse(keyEvent.Key);
        if (keyEvent.IsDown)
        {
            driver.KeyDown(key);
            lock (_heldGate)
            {
                _heldKeys.Add(keyEvent.Key);
            }

            return;
        }

        lock (_heldGate)
        {
            if (!_heldKeys.Remove(keyEvent.Key))
            {
                return;
            }
        }

        driver.KeyUp(key);
    }

    private void ReleaseHeldKeys()
    {
        string[] held;
        lock (_heldGate)
        {
            held = [.. _heldKeys];
            _heldKeys.Clear();
        }

        var driver = _driver;
        if (driver is null)
        {
            return;
        }

        foreach (var name in held)
        {
            try
            {
                driver.KeyUp(InputKey.Parse(name));
            }
            catch (Exception)
            {
            }
        }
    }

    private void StartFocusWatcher(CancellationToken cancellationToken)
    {
        _focusWatcher = new Thread(() => WatchFocus(cancellationToken))
        {
            IsBackground = true,
            Name = "手碟焦点门",
        };
        _focusWatcher.Start();
    }

    private void WatchFocus(CancellationToken cancellationToken)
    {
        var focused = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.WaitHandle.WaitOne(FocusPollMs))
            {
                return;
            }

            var nowFocused = _gameFocused();
            if (nowFocused == focused)
            {
                continue;
            }

            focused = nowFocused;
            if (nowFocused)
            {
                _pauseGate.ReleaseForFocusRegain();
            }
            else
            {
                _pauseGate.HoldForFocusLoss();
            }

            SyncState(PauseSource.FocusLost);
        }
    }

    private void StopFocusWatcher()
    {
        var watcher = _focusWatcher;
        _focusWatcher = null;
        if (watcher is { IsAlive: true })
        {
            watcher.Join(TimeSpan.FromSeconds(1));
        }
    }

    private void SyncState(PauseSource source)
    {
        TaskState? entered = null;
        lock (Gate)
        {
            if (!_pauseGate.IsOpen && CurrentState == TaskState.Running)
            {
                CurrentState = TaskState.Paused;
                entered = TaskState.Paused;
            }
            else if (_pauseGate.IsOpen && CurrentState == TaskState.Paused)
            {
                CurrentState = TaskState.Running;
                entered = TaskState.Running;
            }
        }

        if (entered is null)
        {
            return;
        }

        RaiseStateChanged(entered.Value);
        Raise(entered == TaskState.Paused ? new HandpanPaused(source) : new HandpanResumed(source));
    }

    private void Raise(HandpanTaskEvent taskEvent) => SafeRaiseEvent(taskEvent.AsToolEvent());

    private void DisposeDriver()
    {
        var driver = _driver;
        _driver = null;
        driver?.Dispose();
    }
}
