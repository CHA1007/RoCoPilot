using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class PlaybackClockTests
{
    private sealed class FakeTime
    {
        public double Now { get; set; }

        public Func<double> Reader => () => Now;
    }

    [Fact]
    public void A_fresh_clock_reads_zero_and_is_not_running()
    {
        var clock = new PlaybackClock(new FakeTime().Reader);

        Assert.Equal(0, clock.Elapsed);
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void A_started_clock_follows_the_time_source()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);

        clock.Start();
        time.Now = 1.5;

        Assert.True(clock.IsRunning);
        Assert.Equal(1.5, clock.Elapsed);
    }

    [Fact]
    public void A_paused_clock_stops_accumulating()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);
        clock.Start();
        time.Now = 1;
        clock.Pause();
        time.Now = 9;

        Assert.False(clock.IsRunning);
        Assert.Equal(1, clock.Elapsed);
    }

    [Fact]
    public void Resuming_skips_the_paused_span()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);
        clock.Start();
        time.Now = 1;
        clock.Pause();
        time.Now = 9;
        clock.Resume();
        time.Now = 10;

        Assert.Equal(2, clock.Elapsed);
    }

    [Fact]
    public void Pausing_twice_keeps_the_first_pause_point()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);
        clock.Start();
        time.Now = 1;
        clock.Pause();
        time.Now = 5;
        clock.Pause();

        Assert.Equal(1, clock.Elapsed);
    }

    [Fact]
    public void Resuming_a_running_clock_keeps_its_origin()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);
        clock.Start();
        time.Now = 2;
        clock.Resume();
        time.Now = 3;

        Assert.Equal(3, clock.Elapsed);
    }

    [Fact]
    public void Restarting_clears_the_accumulated_time()
    {
        var time = new FakeTime();
        var clock = new PlaybackClock(time.Reader);
        clock.Start();
        time.Now = 4;
        clock.Pause();
        time.Now = 10;
        clock.Start();

        Assert.Equal(0, clock.Elapsed);
    }

    [Fact]
    public void The_default_clock_reads_a_stopwatch()
    {
        var clock = new PlaybackClock();
        clock.Start();
        Thread.Sleep(20);

        Assert.True(clock.Elapsed > 0);
    }
}
