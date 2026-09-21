using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class PauseGateTests
{
    [Fact]
    public void A_fresh_gate_is_open()
    {
        using var gate = new PauseGate();

        Assert.True(gate.IsOpen);
        Assert.False(gate.IsManuallyHeld);
        Assert.False(gate.IsHeldForFocus);
    }

    [Fact]
    public void A_manual_hold_closes_the_gate()
    {
        using var gate = new PauseGate();

        gate.HoldManually();

        Assert.False(gate.IsOpen);
        Assert.True(gate.IsManuallyHeld);
    }

    [Fact]
    public void A_focus_hold_closes_the_gate()
    {
        using var gate = new PauseGate();

        gate.HoldForFocusLoss();

        Assert.False(gate.IsOpen);
        Assert.True(gate.IsHeldForFocus);
    }

    [Fact]
    public void Regaining_focus_does_not_lift_a_manual_pause()
    {
        using var gate = new PauseGate();
        gate.HoldManually();
        gate.HoldForFocusLoss();

        gate.ReleaseForFocusRegain();

        Assert.False(gate.IsOpen);
        Assert.True(gate.IsManuallyHeld);
    }

    [Fact]
    public void A_manual_resume_does_not_lift_a_focus_hold()
    {
        using var gate = new PauseGate();
        gate.HoldForFocusLoss();

        gate.HoldManually();
        gate.ReleaseManually();

        Assert.False(gate.IsOpen);
        Assert.True(gate.IsHeldForFocus);
    }

    [Fact]
    public void The_gate_opens_once_every_hold_is_lifted()
    {
        using var gate = new PauseGate();
        gate.HoldManually();
        gate.HoldForFocusLoss();

        gate.ReleaseManually();
        gate.ReleaseForFocusRegain();

        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void Waiting_on_an_open_gate_returns_at_once()
    {
        using var gate = new PauseGate();

        gate.WaitUntilOpen(CancellationToken.None);

        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void Waiting_on_a_held_gate_is_cancelled()
    {
        using var gate = new PauseGate();
        gate.HoldManually();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => gate.WaitUntilOpen(cancellation.Token));
    }

    [Fact]
    public async Task Waiting_ends_when_the_gate_opens()
    {
        using var gate = new PauseGate();
        gate.HoldForFocusLoss();

        var release = Task.Run(() =>
        {
            Thread.Sleep(50);
            gate.ReleaseForFocusRegain();
        });
        gate.WaitUntilOpen(CancellationToken.None);

        Assert.True(gate.IsOpen);
        await release.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Waiting_survives_a_hold_released_and_taken_again()
    {
        using var gate = new PauseGate();
        gate.HoldManually();

        var waiter = Task.Run(() => gate.WaitUntilOpen(CancellationToken.None));
        gate.ReleaseManually();
        gate.HoldForFocusLoss();

        Assert.NotEqual(waiter, await Task.WhenAny(waiter, Task.Delay(60)));
        gate.ReleaseForFocusRegain();
        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
