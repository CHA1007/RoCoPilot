using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class PlaybackPlannerTests
{
    private static readonly KeyMap KeyMap = new(KeyMap.DefaultEntries);

    [Fact]
    public void Plan_SchedulesKeysAtBeatTimes()
    {
        var parsed = ScoreParser.Parse("1=C\n3 4 5 5-");
        var plan = PlaybackPlanner.Plan(parsed.Events, 120, KeyMap);

        Assert.Equal(4, plan.Notes.Count);
        Assert.Equal(0, plan.MissingCount);
        Assert.Equal("F", plan.Notes[0].Keys[0]);
        Assert.Equal(0, plan.Notes[0].AtSeconds, 5);
        Assert.Equal(0.5, plan.Notes[1].AtSeconds, 5);
        Assert.Equal(1.5, plan.Notes[3].AtSeconds, 5);
        Assert.Equal(2.5, plan.TotalSeconds, 5);
    }

    [Fact]
    public void Plan_CountsMissingNotes()
    {
        var parsed = ScoreParser.Parse("1=C\n1 2 3");
        var plan = PlaybackPlanner.Plan(parsed.Events, 120, KeyMap);

        Assert.Equal(2, plan.MissingCount);
        Assert.Single(plan.Notes);
    }

    [Fact]
    public void Plan_IncludesExtrasAsChordKeys()
    {
        var e4 = NoteNames.ToMidi("E4");
        var g4 = NoteNames.ToMidi("G4");
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(e4, 1, "x", "E4", [g4, NoteNames.ToMidi("C4")]),
        };

        var plan = PlaybackPlanner.Plan(events, 120, KeyMap);

        var keys = plan.Notes[0].Keys;
        Assert.Equal(["F", "H"], keys);
        Assert.Equal(0.06, plan.Notes[0].HoldSeconds, 5);
    }

    [Fact]
    public void Plan_UsesCustomHolds()
    {
        var parsed = ScoreParser.Parse("1=C\n3");
        var plan = PlaybackPlanner.Plan(parsed.Events, 60, KeyMap, holdSeconds: 0.08, chordHoldSeconds: 0.1);

        Assert.Equal(0.08, plan.Notes[0].HoldSeconds, 5);
        Assert.Equal(1.0, plan.SecondsPerBeat, 5);
    }
}
