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
    private string? _scene;
    private int _routeLap;

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

                case "scene_changed":
                    _scene = toolEvent.Data?.GetValueOrDefault("to") as string;
                    break;

                case "session_stop":
                    _phase = null;
                    break;

                case "route_started":
                    _routeLap = 0;
                    _phase = "路线开始";
                    break;

                case "node_started":
                    _phase = RoutePhase(toolEvent.Data?.GetValueOrDefault("node") as string);
                    break;

                case "segment_done":
                    _phase = RoutePhase("分段完成");
                    break;

                case "anchor_teleport":
                    _phase = RoutePhase(
                        toolEvent.Data?.GetValueOrDefault("phase") as string == "landed" ? "已落地" : "传送中");
                    break;

                case "stuck_retry":
                    _phase = RoutePhase($"重试×{toolEvent.Data?.GetValueOrDefault("attempt") ?? 0}");
                    break;

                case "loop_lap":
                    _routeLap = toolEvent.Data?.GetValueOrDefault("lap") is int lap ? lap : _routeLap;
                    _phase = RoutePhase(null);
                    break;

                case "route_suspended":
                    _phase = "挂起";
                    break;

                case "graph_finished":
                case "route_playback_fault":
                    _routeLap = 0;
                    _phase = null;
                    break;

            }
        }
    }

    private string RoutePhase(string? detail)
    {
        var lap = _routeLap > 0 ? $"第{_routeLap}圈" : "路线";
        return string.IsNullOrEmpty(detail) ? lap : $"{lap}｜{detail}";
    }

    public OverlaySnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = _nowMs();
            var stalled = _stallAlerted && now - _stallRaisedMs <= _stallBannerMs;
            string? banner = stalled ? StallBannerText(_stallSinceSeconds) : null;
            var stallMinutes = stalled ? Math.Max(1, _stallSinceSeconds / 60) : 0;
            return new OverlaySnapshot(_state, _throws, banner, _captureRunning, _phase, _scene, stallMinutes);
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
