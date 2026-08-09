using RocoPilot.Core;

namespace RocoPilot.Loop.Tests;

public class CatchCountersTests
{
    private static ToolEvent Event(string name, object? result = null) =>
        new(name, result is null ? null : new Dictionary<string, object?> { ["result"] = result });

    [Fact]
    public void ConstructorRejectsNonPositiveRateWindow()
    {
        Assert.Throws<LoopException>(() => new CatchCounters(rateWindowMinutes: 0));
        Assert.Throws<LoopException>(() => new CatchCounters(rateWindowMinutes: -5));
    }

    [Fact]
    public void SessionStartEntersRunningAndZeroesCounters()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        var s = c.Snapshot();
        Assert.Equal(CatchLoopState.Running, s.State);
        Assert.Equal(0, s.Throws);
        Assert.Equal(0, s.Settled);
        Assert.Equal(TimeSpan.Zero, s.RunDuration);
        Assert.Equal(TimeSpan.Zero, s.SinceLastSettle);
    }

    [Fact]
    public void TracksThrowsAndSettled()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        c.Record(Event("throw_fired"));
        c.Record(Event("throw_fired"));
        c.Record(Event("settled", "gone"));
        var s = c.Snapshot();
        Assert.Equal(2, s.Throws);
        Assert.Equal(1, s.Settled);
    }

    [Fact]
    public void SettledIgnoredUnlessResultGone()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        c.Record(Event("settled", "kept"));
        c.Record(Event("settled"));
        Assert.Equal(0, c.Snapshot().Settled);
    }

    [Fact]
    public void PauseResumeAccumulatesRunDuration()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        now = 100;
        c.Record(Event("paused"));
        now = 150;
        c.Record(Event("resumed"));
        now = 200;
        c.Record(Event("paused"));
        var s = c.Snapshot();
        Assert.Equal(CatchLoopState.Paused, s.State);
        Assert.Equal(TimeSpan.FromMilliseconds(150), s.RunDuration);
    }

    [Fact]
    public void RunningSnapshotAddsElapsedSinceResume()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        now = 500;
        Assert.Equal(TimeSpan.FromMilliseconds(500), c.Snapshot().RunDuration);
    }

    [Fact]
    public void SessionStopReturnsToIdle()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        c.Record(Event("session_stop"));
        Assert.Equal(CatchLoopState.Idle, c.Snapshot().State);
    }

    [Fact]
    public void ThrowsPerHourDropsAgedThrows()
    {
        long now = 0;
        var c = new CatchCounters(() => now, rateWindowMinutes: 10);
        c.Record(Event("session_start"));
        c.Record(Event("throw_fired"));
        Assert.Equal(6.0, c.Snapshot().ThrowsPerHour, 3);
        now = 600_001;
        Assert.Equal(0.0, c.Snapshot().ThrowsPerHour);
    }

    [Fact]
    public void CenteringRateNullWithoutAcquire()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        Assert.Null(c.Snapshot().CenteringRate);
    }

    [Fact]
    public void CenteringRateIsCenteredOverAcquired()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        c.Record(Event("target_acquired"));
        c.Record(Event("target_acquired"));
        c.Record(Event("centered"));
        Assert.Equal(0.5, c.Snapshot().CenteringRate!.Value, 3);
    }

    [Fact]
    public void SinceLastSettleZeroBeforeAnySession()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        now = 5000;
        Assert.Equal(TimeSpan.Zero, c.Snapshot().SinceLastSettle);
    }

    [Fact]
    public void SinceLastSettleTracksFromSessionStart()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        now = 3000;
        Assert.Equal(TimeSpan.FromMilliseconds(3000), c.Snapshot().SinceLastSettle);
    }

    [Fact]
    public void SettleResetsSinceLastSettle()
    {
        long now = 0;
        var c = new CatchCounters(() => now);
        c.Record(Event("session_start"));
        now = 1000;
        c.Record(Event("settled", "gone"));
        now = 1500;
        Assert.Equal(TimeSpan.FromMilliseconds(500), c.Snapshot().SinceLastSettle);
    }

    [Fact]
    public void AddRestAccumulatesRestDuration()
    {
        var c = new CatchCounters();
        c.AddRest(50);
        c.AddRest(25);
        Assert.Equal(TimeSpan.FromMilliseconds(75), c.Snapshot().RestDuration);
    }
}