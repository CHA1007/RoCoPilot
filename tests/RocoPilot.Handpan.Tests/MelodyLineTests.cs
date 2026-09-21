using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class MelodyLineTests
{
    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    [Fact]
    public void A_line_is_ordered_by_onset_then_by_highest_note()
    {
        var line = MelodyLine.InTimeOrder([Note(2, 3, 60), Note(0, 1, 64), Note(0, 1, 72)]);

        Assert.Equal([72, 64, 60], line.Select(note => note.Pitch));
    }

    [Fact]
    public void An_empty_line_has_no_onset_groups()
    {
        Assert.Empty(MelodyLine.OnsetGroups([]));
    }

    [Fact]
    public void Notes_sharing_a_grid_onset_form_one_group()
    {
        var groups = MelodyLine.OnsetGroups([Note(0, 1, 60), Note(0.01, 1, 64), Note(1, 2, 67)]);

        Assert.Equal(2, groups.Count);
        Assert.Equal([64, 60], groups[0].Select(note => note.Pitch));
        Assert.Equal([67], groups[1].Select(note => note.Pitch));
    }

    [Fact]
    public void Groups_are_ordered_by_onset()
    {
        var groups = MelodyLine.OnsetGroups([Note(2, 3, 60), Note(0, 1, 64)]);

        Assert.Equal([64, 60], groups.Select(group => group[0].Pitch));
    }

    [Fact]
    public void An_empty_line_spans_no_beats()
    {
        Assert.Equal(0, MelodyLine.TotalBeats([]));
    }

    [Fact]
    public void The_line_spans_to_its_latest_note_end()
    {
        Assert.Equal(4, MelodyLine.TotalBeats([Note(0, 4, 60), Note(2, 3, 64)]));
    }
}
