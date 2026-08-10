using RocoPilot.Tools.FastTravel;

namespace RocoPilot.Shell.Tests;

public class TeleportRequestGateTests
{
    [Fact]
    public void NewRequest_IsPending()
    {
        var gate = new TeleportRequestGate();

        gate.Request();

        Assert.True(gate.Pending);
    }

    [Fact]
    public void NewGate_HasNoPendingRequest()
    {
        var gate = new TeleportRequestGate();

        Assert.False(gate.Pending);
    }

    [Fact]
    public void Consume_ClearsPending()
    {
        var gate = new TeleportRequestGate();
        gate.Request();

        gate.Consume();

        Assert.False(gate.Pending);
    }

    [Fact]
    public void RequestAfterConsume_IsPendingAgain()
    {
        var gate = new TeleportRequestGate();
        gate.Request();
        gate.Consume();

        gate.Request();

        Assert.True(gate.Pending);
    }

    [Fact]
    public void ConsumeOnEmptyGate_IsHarmless()
    {
        var gate = new TeleportRequestGate();

        gate.Consume();

        Assert.False(gate.Pending);
    }
}