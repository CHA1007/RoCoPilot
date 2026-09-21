namespace RocoPilot.Handpan.Tests;

public class ProbePlannerTests
{
    [Fact]
    public void The_schedule_uses_the_measured_constants()
    {
        Assert.Equal(0.9, ProbePlanner.IntervalSeconds);
        Assert.Equal(0.12, ProbePlanner.PressSeconds);
    }

    [Fact]
    public void The_first_key_sounds_immediately_and_the_rest_follow_the_interval()
    {
        var plan = ProbePlanner.Plan(new KeyMap([new KeyMapEntry("A3", "B"), new KeyMapEntry("C5", "T")]));

        Assert.Equal(
            [new ProbeKey("B", "A3", 0, 0.12), new ProbeKey("T", "C5", 0.9, 1.02)],
            plan);
    }

    [Fact]
    public void The_plan_walks_the_keys_in_pitch_order()
    {
        var plan = ProbePlanner.Plan(new KeyMap(
        [
            new KeyMapEntry("E5", "U"),
            new KeyMapEntry("A3", "B"),
            new KeyMapEntry("D5", "Y"),
        ]));

        Assert.Equal(["A3", "D5", "E5"], plan.Select(key => key.Note));
        Assert.Equal([0, 0.9, 1.8], plan.Select(key => key.DownAtSeconds));
        Assert.Equal([0.12, 1.02, 1.92], plan.Select(key => key.UpAtSeconds));
    }

    [Fact]
    public void Invalid_entries_take_no_slot_in_the_schedule()
    {
        var plan = ProbePlanner.Plan(new KeyMap([new KeyMapEntry("X9", "Q"), new KeyMapEntry("C5", "T")]));

        Assert.Equal([new ProbeKey("T", "C5", 0, 0.12)], plan);
    }

    [Fact]
    public void An_unusable_map_yields_no_keys()
    {
        Assert.Empty(ProbePlanner.Plan(new KeyMap([new KeyMapEntry("X9", "Q")])));
        Assert.Empty(ProbePlanner.Plan(new KeyMap([])));
    }

    [Fact]
    public void Note_names_are_canonical_regardless_of_spelling()
    {
        var plan = ProbePlanner.Plan(new KeyMap([new KeyMapEntry("Bb4", "G")]));

        Assert.Equal("A#4", Assert.Single(plan).Note);
    }
}
