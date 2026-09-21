using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class MelodyFittingTests
{
    private static readonly KeyMap DefaultMap = new(KeyMap.DefaultEntries);

    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    private static int[] Pitches(IReadOnlyList<MidiNote> notes) => [.. notes.Select(note => note.Pitch)];

    [Fact]
    public void Zero_transposition_returns_the_same_line()
    {
        var line = new[] { Note(0, 1, 72) };

        Assert.Same(line, MelodyFitting.Transpose(line, 0));
    }

    [Fact]
    public void Transposition_shifts_pitch_and_keeps_the_timeline()
    {
        var line = MelodyFitting.Transpose([Note(1, 2.5, 72)], -3);

        Assert.Equal(new MidiNote(0, 69, 1, 2.5), Assert.Single(line));
    }

    [Theory]
    [InlineData(125, 11, 127)]
    [InlineData(3, -11, 0)]
    public void Transposition_stays_inside_the_midi_range(int pitch, int semitones, int expected)
    {
        Assert.Equal(expected, Assert.Single(MelodyFitting.Transpose([Note(0, 1, pitch)], semitones)).Pitch);
    }

    [Fact]
    public void A_non_positive_gap_returns_the_same_line()
    {
        var line = new[] { Note(0, 1, 72) };

        Assert.Same(line, MelodyFitting.Gap(line, 0));
        Assert.Same(line, MelodyFitting.Gap(line, -0.5));
    }

    [Fact]
    public void Every_onset_is_delayed_by_one_more_gap()
    {
        var line = MelodyFitting.Gap([Note(0, 1, 72), Note(1, 2, 74), Note(2, 3, 76)], 0.25);

        Assert.Equal([0, 1.25, 2.5], line.Select(note => note.StartBeat));
        Assert.Equal([1, 1, 1], line.Select(note => note.EndBeat - note.StartBeat));
    }

    [Fact]
    public void Notes_sharing_an_onset_share_one_gap()
    {
        var line = MelodyFitting.Gap([Note(0, 1, 72), Note(0, 1, 76), Note(1, 2, 74)], 0.5);

        Assert.Equal([0, 0, 1.5], line.Select(note => note.StartBeat));
    }

    [Fact]
    public void Onsets_within_a_grid_unit_share_one_gap()
    {
        var line = MelodyFitting.Gap([Note(0, 1, 72), Note(0.01, 1, 74)], 0.5);

        Assert.Equal([0, 0.01], line.Select(note => note.StartBeat));
    }

    [Fact]
    public void Gap_orders_the_line_by_time()
    {
        var line = MelodyFitting.Gap([Note(1, 2, 74), Note(0, 1, 72)], 0.5);

        Assert.Equal([72, 74], Pitches(line));
        Assert.Equal([0, 1.5], line.Select(note => note.StartBeat));
    }

    [Fact]
    public void An_empty_line_fits_to_nothing()
    {
        var fit = MelodyFitting.Fit([], DefaultMap);

        Assert.Empty(fit.Notes);
        Assert.Equal(0, fit.Semitones);
        Assert.Equal(0, fit.ExactCoverage);
        Assert.True(fit.IsComplete);
    }

    [Fact]
    public void A_line_inside_the_key_range_is_left_alone()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 72), Note(1, 2, 74)], DefaultMap);

        Assert.Equal([72, 74], Pitches(fit.Notes));
        Assert.Equal(0, fit.Semitones);
        Assert.Equal(1, fit.ExactCoverage);
        Assert.Equal(0, fit.FoldedCount);
        Assert.Equal(0, fit.SnappedCount);
        Assert.True(fit.IsComplete);
    }

    [Fact]
    public void A_note_above_the_range_folds_down_an_octave()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 84)], DefaultMap, transpose: 0);

        Assert.Equal([72], Pitches(fit.Notes));
        Assert.Equal(1, fit.FoldedCount);
        Assert.Equal(1, fit.ExactCoverage);
    }

    [Fact]
    public void The_first_fold_takes_the_nearest_octave_candidate()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 88)], DefaultMap);

        Assert.Equal([76], Pitches(fit.Notes));
    }

    [Fact]
    public void Later_folds_follow_the_previous_melody_note()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 64), Note(1, 2, 88)], DefaultMap);

        Assert.Equal([64, 64], Pitches(fit.Notes));
        Assert.Equal(1, fit.FoldedCount);
    }

    [Fact]
    public void Folding_keeps_the_timeline()
    {
        var fit = MelodyFitting.Fit([Note(2, 3.5, 84)], DefaultMap, transpose: 0);

        Assert.Equal(new MidiNote(0, 72, 2, 3.5), Assert.Single(fit.Notes));
    }

    [Fact]
    public void A_chromatic_note_snaps_to_the_nearest_key_below()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 73)], DefaultMap, transpose: 0);

        Assert.Equal([72], Pitches(fit.Notes));
        Assert.Equal(1, fit.SnappedCount);
    }

    [Fact]
    public void Snapping_prefers_a_scale_tone_of_the_tonic()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 73)], DefaultMap, tonic: "D", transpose: 0);

        Assert.Equal([74], Pitches(fit.Notes));
    }

    [Fact]
    public void Snapping_reaches_over_an_octave_when_that_is_closer()
    {
        var sparse = new KeyMap([new KeyMapEntry("A3", "B"), new KeyMapEntry("E5", "U")]);

        var fit = MelodyFitting.Fit([Note(0, 1, 68)], sparse, transpose: 0);

        Assert.Equal([57], Pitches(fit.Notes));
        Assert.Equal(1, fit.SnappedCount);
    }

    [Fact]
    public void The_wide_snap_pass_catches_notes_outside_the_narrow_range()
    {
        var sparse = new KeyMap([new KeyMapEntry("C5", "T")]);

        var fit = MelodyFitting.Fit([Note(0, 1, 76)], sparse, transpose: 0);

        Assert.Equal([72], Pitches(fit.Notes));
        Assert.Equal(1, fit.SnappedCount);
    }

    [Fact]
    public void A_note_beyond_every_snap_range_stays_missing()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 30)], DefaultMap, transpose: 0);

        Assert.Equal([30], Pitches(fit.Notes));
        Assert.False(fit.IsComplete);
        Assert.Equal(1, fit.MissingCount);
        Assert.Equal(Note(0, 1, 30), Assert.Single(fit.MissingNotes));
    }

    [Fact]
    public void Coverage_is_weighted_by_note_length()
    {
        var coverage = MelodyFitting.ExactCoverage([Note(0, 3, 72), Note(3, 4, 73)], DefaultMap);

        Assert.Equal(0.75, coverage);
    }

    [Fact]
    public void Zero_length_notes_are_covered_by_count()
    {
        var coverage = MelodyFitting.ExactCoverage([Note(0, 0, 72), Note(0, 0, 73)], DefaultMap);

        Assert.Equal(0.5, coverage);
    }

    [Fact]
    public void An_empty_line_has_no_coverage()
    {
        Assert.Equal(0, MelodyFitting.ExactCoverage([], DefaultMap));
    }

    [Fact]
    public void The_transposition_with_the_best_exact_coverage_wins()
    {
        var line = new[] { Note(0, 1, 62), Note(1, 2, 65), Note(2, 3, 67) };

        var fit = MelodyFitting.Fit(line, DefaultMap);

        Assert.Equal(2, fit.Semitones);
        Assert.Equal([64, 67, 69], Pitches(fit.Notes));
        Assert.Equal(1, fit.ExactCoverage);
    }

    [Fact]
    public void Equal_coverage_prefers_the_smaller_transposition_distance()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 60)], DefaultMap);

        Assert.Equal(-3, fit.Semitones);
        Assert.Equal([57], Pitches(fit.Notes));
    }

    [Fact]
    public void An_explicit_transposition_skips_the_search()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 72)], DefaultMap, transpose: -5);

        Assert.Equal(-5, fit.Semitones);
        Assert.Equal([67], Pitches(fit.Notes));
        Assert.Equal(1, fit.ExactCoverage);
    }

    [Fact]
    public void Coverage_of_an_explicit_transposition_is_measured_after_folding()
    {
        var fit = MelodyFitting.Fit([Note(0, 1, 72), Note(1, 2, 84)], DefaultMap, transpose: 0);

        Assert.Equal(1, fit.ExactCoverage);
        Assert.Equal(1, fit.FoldedCount);
    }

    [Fact]
    public void Missing_notes_keep_the_input_order()
    {
        var missing = MelodyFitting.Missing([Note(2, 3, 73), Note(0, 1, 72), Note(1, 2, 30)], DefaultMap);

        Assert.Equal([73, 30], Pitches(missing));
    }

    [Fact]
    public void Fitting_orders_the_line_by_time()
    {
        var fit = MelodyFitting.Fit([Note(1, 2, 74), Note(0, 1, 72)], DefaultMap);

        Assert.Equal([72, 74], Pitches(fit.Notes));
    }
}
