using RocoPilot.Core;

namespace RocoPilot.Loop.Tests;

public class CatchEventBusTests
{
    [Fact]
    public void ConstructorRejectsNullCounters()
    {
        Assert.Throws<ArgumentNullException>(() => new CatchEventBus(null!));
    }

    [Fact]
    public void EmitForwardsToCounters()
    {
        var counters = new CatchCounters();
        var bus = new CatchEventBus(counters);
        bus.Emit("session_start");
        Assert.Equal(CatchLoopState.Running, counters.Snapshot().State);
        bus.Emit("throw_fired");
        Assert.Equal(1, counters.Snapshot().Throws);
    }

    [Fact]
    public void EmitReturnsTheEvent()
    {
        var bus = new CatchEventBus(new CatchCounters());
        var ev = bus.Emit("ping");
        Assert.Equal("ping", ev.Name);
    }

    [Fact]
    public void ForwardNullThrows()
    {
        var bus = new CatchEventBus(new CatchCounters());
        Assert.Throws<ArgumentNullException>(() => bus.Forward(null!));
    }

    [Fact]
    public void DispatchInvokesEventRaisedSubscribers()
    {
        var bus = new CatchEventBus(new CatchCounters());
        ToolEvent? received = null;
        bus.EventRaised += (_, e) => received = e;
        var ev = bus.Emit("ping");
        Assert.Same(ev, received);
    }

    [Fact]
    public void HandlerExceptionIsSwallowed()
    {
        var bus = new CatchEventBus(new CatchCounters());
        bus.EventRaised += (_, _) => throw new InvalidOperationException();
        bus.Emit("ping");
    }

    [Fact]
    public void JsonlPathNullWithoutSink()
    {
        var bus = new CatchEventBus(new CatchCounters());
        Assert.Null(bus.JsonlPath);
    }

    [Fact]
    public void CountersPropertyExposesBackingCounters()
    {
        var counters = new CatchCounters();
        var bus = new CatchEventBus(counters);
        Assert.Same(counters, bus.Counters);
    }
}