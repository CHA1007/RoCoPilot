using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class AccompanimentTests
{
    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    private static MelodySelection Selection(params (string Name, MidiNote[] Notes)[] parts)
    {
        var profiles = new List<PartProfile>();
        for (var index = 0; index < parts.Length; index++)
        {
            var notes = parts[index].Notes.OrderBy(note => note.StartBeat).ToList();
            var latestEnd = double.MinValue;
            var overlapped = 0;
            foreach (var note in notes)
            {
                if (note.StartBeat < latestEnd)
                {
                    overlapped++;
                }

                latestEnd = Math.Max(latestEnd, note.EndBeat);
            }

            profiles.Add(new PartProfile(
                index,
                parts[index].Name,
                notes,
                (double)overlapped / notes.Count,
                notes.Average(note => note.Pitch),
                0));
        }

        var melody = profiles[0];
        return new MelodySelection(profiles, melody, melody.Notes);
    }

    [Fact]
    public void A_polyphonic_track_below_the_melody_is_picked()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72), Note(1, 2, 74)]),
            ("和弦", [Note(0, 4, 57), Note(0, 4, 64), Note(4, 8, 57), Note(4, 8, 64)]),
            ("低音", [Note(0, 1, 40), Note(4, 5, 40)]));

        var track = Accompaniment.PickTrack(selection);

        Assert.Equal("和弦", track!.Name);
    }

    [Fact]
    public void A_track_at_or_above_the_melody_is_skipped()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72)]),
            ("高音华彩", [Note(0, 1, 88)]));

        Assert.Null(Accompaniment.PickTrack(selection));
    }

    [Fact]
    public void A_melody_only_score_has_no_accompaniment()
    {
        var selection = Selection(("主旋律", [Note(0, 1, 72)]));

        Assert.Null(Accompaniment.PickTrack(selection));
    }

    [Fact]
    public void Onsets_are_quantized_onto_the_grid()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72)]),
            ("和弦", [Note(0.1, 1, 57), Note(1.9, 3, 57)]));

        var line = Accompaniment.Extract(selection.Profiles[1], 2);

        Assert.Equal([0, 2], line.Select(note => note.StartBeat));
    }

    [Fact]
    public void Groups_landing_on_the_same_grid_point_merge()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72)]),
            ("和弦", [Note(0.1, 1, 55), Note(0.2, 1, 62), Note(0.3, 1, 69)]));

        var line = Accompaniment.Extract(selection.Profiles[1], 2);

        Assert.Equal([55, 69], line.Select(note => note.Pitch));
        Assert.All(line, note => Assert.Equal(0, note.StartBeat));
    }

    [Fact]
    public void Each_stab_spans_one_grid_cell()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72)]),
            ("和弦", [Note(0.1, 5, 57), Note(0.1, 5, 64)]));

        var line = Accompaniment.Extract(selection.Profiles[1], 2);

        Assert.All(line, note => Assert.Equal(2, note.EndBeat - note.StartBeat));
    }

    [Fact]
    public void A_non_positive_grid_yields_nothing()
    {
        var selection = Selection(
            ("主旋律", [Note(0, 1, 72)]),
            ("和弦", [Note(0.1, 1, 57)]));

        Assert.Empty(Accompaniment.Extract(selection.Profiles[1], 0));
        Assert.Empty(Accompaniment.Extract(selection.Profiles[1], -1));
    }
}
