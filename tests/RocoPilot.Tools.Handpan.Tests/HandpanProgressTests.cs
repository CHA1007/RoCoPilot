namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanProgressTests
{
    [Fact]
    public void The_ratio_is_the_elapsed_share_of_the_whole()
    {
        Assert.Equal(0.25, new HandpanProgress(2.5, 10, 0).Ratio);
        Assert.Equal(0, new HandpanProgress(0, 10, 0).Ratio);
    }

    [Fact]
    public void An_overrun_is_clamped_and_an_empty_plan_reads_as_empty()
    {
        Assert.Equal(1, new HandpanProgress(12, 10, 0).Ratio);
        Assert.Equal(0, new HandpanProgress(5, 0, 0).Ratio);
    }

    [Fact]
    public void Rounds_are_shown_one_based_and_only_a_repeated_round_loops()
    {
        Assert.Equal(1, new HandpanProgress(1, 10, 0).RoundNumber);
        Assert.False(new HandpanProgress(1, 10, 0).IsLooping);
        Assert.Equal(3, new HandpanProgress(1, 10, 2).RoundNumber);
        Assert.True(new HandpanProgress(1, 10, 2).IsLooping);
    }
}
