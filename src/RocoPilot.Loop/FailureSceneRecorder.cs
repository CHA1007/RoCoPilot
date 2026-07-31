using System.Collections.Concurrent;
using System.Diagnostics;
using RocoPilot.Core;

namespace RocoPilot.Loop;

public sealed class FailureSceneRecorder : IDisposable
{
    private const int RecentEventCapacity = 32;
    private const int QueueCapacity = 8;
    private const int DefaultCooldownMs = 2000;
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(3);

    private readonly SceneStore _store;
    private readonly Func<FrameSnapshot?> _grabFrame;
    private readonly int _cooldownMs;
    private readonly Func<long> _nowMs;
    private readonly BlockingCollection<WorkItem> _queue = new(QueueCapacity);
    private readonly Thread _writer;
    private readonly object _gate = new();
    private readonly Queue<ToolEvent> _recentEvents = new();
    private readonly Dictionary<string, long> _lastCaptureMs = new();

    private CatchEventBus? _bus;
    private int _dropped;

    public FailureSceneRecorder(
        SceneStore store,
        Func<FrameSnapshot?> grabFrame,
        int cooldownMs = DefaultCooldownMs,
        Func<long>? nowMs = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _grabFrame = grabFrame ?? throw new ArgumentNullException(nameof(grabFrame));
        _cooldownMs = cooldownMs;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "失败现场落盘" };
        _writer.Start();
    }

    public int DroppedCount => Volatile.Read(ref _dropped);

    public void AttachBus(CatchEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        lock (_gate)
        {
            if (ReferenceEquals(_bus, bus))
            {
                return;
            }

            if (_bus is not null)
            {
                _bus.EventRaised -= OnBusEvent;
            }

            _bus = bus;
        }

        bus.EventRaised += OnBusEvent;
    }

    public void Capture(string trigger, ToolEvent cause)
    {
        ArgumentException.ThrowIfNullOrEmpty(trigger);
        ArgumentNullException.ThrowIfNull(cause);
        Enqueue(trigger, cause);
    }

    public void Dispose()
    {
        if (_bus is { } bus)
        {
            bus.EventRaised -= OnBusEvent;
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _writer.Join(DisposeDrainTimeout);
        _queue.Dispose();
    }

    private void OnBusEvent(object? sender, ToolEvent toolEvent)
    {
        lock (_gate)
        {
            _recentEvents.Enqueue(toolEvent);
            while (_recentEvents.Count > RecentEventCapacity)
            {
                _recentEvents.Dequeue();
            }
        }

        var trigger = toolEvent.Name switch
        {
            "settled" when string.Equals(
                toolEvent.Data?.GetValueOrDefault("result") as string, "present", StringComparison.Ordinal) => "miss",
            "centering_failed" when string.Equals(
                toolEvent.Data?.GetValueOrDefault("reason") as string, "lost", StringComparison.Ordinal) => "fled",
            "calibration" when string.Equals(
                toolEvent.Data?.GetValueOrDefault("source") as string, "failed", StringComparison.Ordinal) => "calibration",
            _ => null,
        };
        if (trigger is not null)
        {
            Enqueue(trigger, toolEvent);
        }
    }

    private void Enqueue(string trigger, ToolEvent cause)
    {
        lock (_gate)
        {
            var now = _nowMs();
            if (_cooldownMs > 0 &&
                _lastCaptureMs.TryGetValue(trigger, out var last) &&
                now - last < _cooldownMs)
            {
                return;
            }

            _lastCaptureMs[trigger] = now;
        }

        var frame = SafeGrabFrame();
        if (frame is null)
        {
            return;
        }

        CatchCountersSnapshot? counters = null;
        try
        {
            counters = _bus?.Counters.Snapshot();
        }
        catch
        {
        }

        List<ToolEvent> tail;
        lock (_gate)
        {
            tail = _recentEvents.ToList();
        }

        var item = new WorkItem(trigger, cause, frame, counters, tail);
        try
        {
            if (!_queue.TryAdd(item))
            {
                Interlocked.Increment(ref _dropped);
                Trace.TraceWarning($"失败现场队列已满，丢弃本次（trigger={trigger}）");
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private FrameSnapshot? SafeGrabFrame()
    {
        try
        {
            return _grabFrame();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"失败现场抓帧失败：{ex.GetBaseException().Message}");
            return null;
        }
    }

    private void WriterLoop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                _store.Save(item.Trigger, item.Cause, item.Frame, BuildSidecar(item));
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"失败现场写线程意外：{ex.GetBaseException().Message}");
        }
    }

    private static Dictionary<string, object?> BuildSidecar(WorkItem item)
    {
        var frame = item.Frame;
        var sidecar = new Dictionary<string, object?>
        {
            ["trigger"] = item.Trigger,
            ["triggered_at"] = item.Cause.Timestamp.ToString("O"),
            ["event"] = new Dictionary<string, object?>
            {
                ["name"] = item.Cause.Name,
                ["timestamp"] = item.Cause.Timestamp.ToString("O"),
                ["data"] = item.Cause.Data,
            },
            ["frame"] = new Dictionary<string, object?>
            {
                ["width"] = frame.Width,
                ["height"] = frame.Height,
                ["sequence"] = frame.Sequence,
                ["captured_at"] = frame.CapturedAt.ToString("O"),
            },
            ["detections"] = frame.Detections.Select(box => (object?)new Dictionary<string, object?>
            {
                ["cls"] = box.ClassName,
                ["conf"] = Math.Round(box.Confidence, 3),
                ["x1"] = box.X1,
                ["y1"] = box.Y1,
                ["x2"] = box.X2,
                ["y2"] = box.Y2,
            }).ToList(),
        };

        if (item.Counters is { } counters)
        {
            sidecar["counters"] = new Dictionary<string, object?>
            {
                ["state"] = counters.State.ToString(),
                ["throws"] = counters.Throws,
                ["settled"] = counters.Settled,
                ["throws_per_hour"] = Math.Round(counters.ThrowsPerHour, 1),
                ["centering_rate"] = counters.CenteringRate is { } rate ? Math.Round(rate, 3) : null,
                ["run_duration_s"] = (int)counters.RunDuration.TotalSeconds,
                ["since_last_settle_s"] = (int)counters.SinceLastSettle.TotalSeconds,
            };
        }

        sidecar["recent_events"] = item.RecentTail.Select(toolEvent =>
        {
            var row = new Dictionary<string, object?>
            {
                ["t"] = toolEvent.Timestamp.ToUnixTimeMilliseconds() / 1000.0,
                ["event"] = toolEvent.Name,
            };
            if (toolEvent.Data is { } data)
            {
                foreach (var (key, value) in data)
                {
                    row[key] = value;
                }
            }

            return (object?)row;
        }).ToList();

        return sidecar;
    }

    private sealed record WorkItem(
        string Trigger,
        ToolEvent Cause,
        FrameSnapshot Frame,
        CatchCountersSnapshot? Counters,
        List<ToolEvent> RecentTail);
}
