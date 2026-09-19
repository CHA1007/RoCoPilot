using System.Diagnostics;
using RocoPilot.Core;
using RocoPilot.Handpan;
using RocoPilot.Input;

namespace RocoPilot.Tools.Handpan;

public sealed class HandpanRunningTask : RunningTaskBase
{
    private const int SleepSliceMs = 20;

    private readonly HandpanSettings _settings;
    private readonly HandpanTaskMode _mode;
    private readonly Func<IInputDriver> _driverFactory;
    private readonly ManualResetEventSlim _resumeGate = new(true);
    private IInputDriver? _driver;

    public HandpanRunningTask(HandpanSettings settings, HandpanTaskMode mode, Func<IInputDriver> driverFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mode = mode;
        _driverFactory = driverFactory ?? throw new ArgumentNullException(nameof(driverFactory));
    }

    public override string ToolId => HandpanTool.ToolId;

    public override void RequestPause(string source = "manual")
    {
        lock (Gate)
        {
            if (CurrentState != TaskState.Running)
            {
                return;
            }

            CurrentState = TaskState.Paused;
        }

        _resumeGate.Reset();
        RaiseStateChanged(TaskState.Paused);
    }

    public override void RequestResume(string source = "manual")
    {
        lock (Gate)
        {
            if (CurrentState != TaskState.Paused)
            {
                return;
            }

            CurrentState = TaskState.Running;
        }

        _resumeGate.Set();
        RaiseStateChanged(TaskState.Running);
    }

    protected override async Task RunWorkerAsync(CancellationToken ct)
    {
        try
        {
            if (!TryEnterRunning())
            {
                return;
            }

            RaiseStateChanged(TaskState.Running);
            await Task.Run(() =>
            {
                switch (_mode)
                {
                    case HandpanTaskMode.Chart: RunChartOnly(ct); break;
                    case HandpanTaskMode.Probe: RunProbe(ct); break;
                    default: RunPlay(ct); break;
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RaiseEvent(new ToolEvent("error", new Dictionary<string, object?> { ["message"] = ex.Message }));
        }
        finally
        {
            FinishStopped();
        }
    }

    protected override void DisposeCore()
    {
        _driver?.Dispose();
        _driver = null;
        _resumeGate.Dispose();
    }

    private HandpanPipelineResult? BuildPipeline()
    {
        try
        {
            var score = ScoreParser.Parse(_settings.ScoreText);
            var options = new HandpanOptions
            {
                BpmOverride = _settings.BpmOverride,
                Transpose = _settings.Transpose,
                AutoFit = _settings.AutoFit,
                Fold = _settings.Fold,
                Snap = _settings.Snap,
                GapBeats = _settings.GapBeats,
            };
            return HandpanPipeline.Run(score, _settings.ToKeyMap(), options);
        }
        catch (ScoreParseException ex)
        {
            RaiseEvent(new ToolEvent("error", new Dictionary<string, object?> { ["message"] = ex.Message }));
            return null;
        }
    }

    private void RunChartOnly(CancellationToken ct)
    {
        var result = BuildPipeline();
        if (result is null)
        {
            return;
        }

        PublishResult(result);
        Log("按键谱已生成。");
    }

    private void RunPlay(CancellationToken ct)
    {
        var result = BuildPipeline();
        if (result is null)
        {
            return;
        }

        PublishResult(result);
        if (result.Plan.Notes.Count == 0)
        {
            Log("没有可演奏的音符。");
            return;
        }

        Log($"准备演奏：{result.Plan.Notes.Count} 个音符，BPM={result.Meta.Bpm:0.##}，时长≈{result.Plan.TotalSeconds:0.#} 秒");
        Countdown(ct);

        var round = 0;
        while (true)
        {
            var clock = Stopwatch.StartNew();
            foreach (var note in result.Plan.Notes)
            {
                ct.ThrowIfCancellationRequested();
                var wait = note.AtSeconds - clock.Elapsed.TotalSeconds;
                if (wait > 0)
                {
                    Sleep(wait, ct);
                }

                PressKeys(note.Keys, note.HoldSeconds);
            }

            if (!_settings.Loop)
            {
                break;
            }

            round++;
            Log($"第 {round} 遍完成，继续循环…");
            Sleep(1.5, ct);
        }

        Log("演奏完成。");
    }

    private void RunProbe(CancellationToken ct)
    {
        var keyMap = _settings.ToKeyMap();
        Log("键位自检：每 0.9 秒自动按一个键，请切到游戏窗口听声音。");
        Countdown(ct);

        foreach (var midi in keyMap.SortedMidiNotes)
        {
            ct.ThrowIfCancellationRequested();
            keyMap.TryGet(midi, out var key);
            var degree = NoteNames.DegreeZh(midi);
            Log($"按键 [{key}] → 应该发出 {NoteNames.Of(midi)}{(degree is null ? string.Empty : $"（{degree}）")}");
            PressKeys([key], 0.12);
            Sleep(0.9, ct);
        }

        Log("自检完成。如映射不对，请在键位卡片里修改后重试。");
    }

    private void PublishResult(HandpanPipelineResult result)
    {
        RaiseEvent(new ToolEvent("chart", new Dictionary<string, object?>
        {
            ["text"] = result.Chart.Text,
            ["missing"] = result.MissingCount,
            ["notes"] = result.Plan.Notes.Count,
        }));
        foreach (var line in result.ReportLines)
        {
            Log(line);
        }
    }

    private void Countdown(CancellationToken ct)
    {
        var seconds = Math.Clamp(_settings.CountdownSeconds, 0, 10);
        if (seconds == 0)
        {
            return;
        }

        Log($"{seconds} 秒后开始，请切到游戏窗口…");
        for (var i = seconds; i > 0; i--)
        {
            Log($"  {i}…");
            Sleep(1.0, ct);
        }
    }

    private void Sleep(double seconds, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            if (!_resumeGate.IsSet)
            {
                _resumeGate.Wait(ct);
                continue;
            }

            Thread.Sleep(SleepSliceMs);
        }
    }

    private void PressKeys(IReadOnlyList<string> keys, double holdSeconds)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var driver = _driver ??= _driverFactory();
        var inputs = keys.Select(ToInputKey).ToList();
        if (inputs.Count == 1)
        {
            driver.KeyDown(inputs[0]);
            Thread.Sleep((int)(holdSeconds * 1000));
            driver.KeyUp(inputs[0]);
            return;
        }

        var stagger = _settings.ChordStaggerMs;
        var pressed = new List<InputKey>(inputs.Count);
        foreach (var input in inputs)
        {
            driver.KeyDown(input);
            pressed.Add(input);
            if (stagger > 0)
            {
                Thread.Sleep(stagger);
            }
        }

        Thread.Sleep((int)(holdSeconds * 1000));
        foreach (var input in Enumerable.Reverse(pressed))
        {
            driver.KeyUp(input);
        }
    }

    private static InputKey ToInputKey(string key)
    {
        var ch = key.Length > 0 ? key[0] : ' ';
        var vk = char.IsAsciiDigit(ch) ? ch : char.ToUpperInvariant(ch);
        return InputKey.Keyboard(vk);
    }

    private void Log(string text) =>
        RaiseEvent(new ToolEvent("log", new Dictionary<string, object?> { ["text"] = text }));
}
