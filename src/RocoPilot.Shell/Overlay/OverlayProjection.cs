using RocoPilot.Core;

namespace RocoPilot.Shell.Overlay;

public sealed class OverlayProjection
{
    public const long DefaultStallBannerMs = 8_000;

    private readonly Func<long> _nowMs;
    private readonly long _stallBannerMs;
    private readonly object _gate = new();

    private TaskState _state = TaskState.Idle;
    private bool _captureRunning;
    private int _throws;
    private string? _phase;
    private bool _stallAlerted;
    private long _stallRaisedMs;
    private int _stallSinceSeconds;

    public OverlayProjection(Func<long>? nowMs = null, long stallBannerMs = DefaultStallBannerMs)
    {
        if (stallBannerMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stallBannerMs), "横幅可见窗须为正");
        }

        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _stallBannerMs = stallBannerMs;
    }

    public void ApplyState(TaskState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }

    public void ApplyCapture(bool running)
    {
        lock (_gate)
        {
            _captureRunning = running;
        }
    }

    public void ApplyEvent(ToolEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);
        lock (_gate)
        {
            var now = _nowMs();
            switch (toolEvent.Name)
            {
                case "session_start":
                    _throws = 0;
                    _stallAlerted = false;
                    _phase = "扫描";
                    break;

                case "target_acquired":
                    _stallAlerted = false;
                    _phase = "转向";
                    break;

                case "throw_fired":
                    _throws++;
                    _phase = "投掷";
                    break;

                case "settled":
                    if (IsGone(toolEvent))
                    {
                        _stallAlerted = false;
                    }

                    _phase = "扫描";
                    break;

                case "stall_alert":
                    _stallAlerted = true;
                    _stallRaisedMs = now;
                    _stallSinceSeconds = toolEvent.Data?.GetValueOrDefault("since_settle_s") is int s ? s : 0;
                    break;

                case "session_stop":
                    _phase = null;
                    break;

            }
        }
    }

    public OverlaySnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = _nowMs();
            string? banner = _stallAlerted && now - _stallRaisedMs <= _stallBannerMs
                ? StallBannerText(_stallSinceSeconds)
                : null;
            return new OverlaySnapshot(_state, _throws, banner, _captureRunning, _phase);
        }
    }

    private static bool IsGone(ToolEvent toolEvent) =>
        string.Equals(toolEvent.Data?.GetValueOrDefault("result") as string, "gone", StringComparison.Ordinal);

    private static string StallBannerText(int sinceSeconds)
    {
        var minutes = Math.Max(1, sinceSeconds / 60);
        return $"⚠ 僵住：已 {minutes} 分钟无了结——仅通知，不停机";
    }
}
