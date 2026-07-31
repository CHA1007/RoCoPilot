using RocoPilot.Core;

namespace RocoPilot.Loop;

public sealed class CatchEventBus
{
    private readonly CatchCounters _counters;
    private readonly JsonlEventSink? _sink;

    public CatchEventBus(CatchCounters counters, JsonlEventSink? sink = null)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _sink = sink;
    }

    public CatchCounters Counters => _counters;

    public string? JsonlPath => _sink?.FilePath;

    public event EventHandler<ToolEvent>? EventRaised;

    public ToolEvent Emit(string name, IReadOnlyDictionary<string, object?>? data = null) =>
        Dispatch(new ToolEvent(name, data));

    public ToolEvent Forward(ToolEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);
        return Dispatch(toolEvent);
    }

    private ToolEvent Dispatch(ToolEvent toolEvent)
    {
        _counters.Record(toolEvent);
        _sink?.Write(toolEvent);

        var handlers = EventRaised;
        if (handlers is not null)
        {
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<ToolEvent>)handler)(this, toolEvent);
                }
                catch
                {
                }
            }
        }

        return toolEvent;
    }
}
