using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class TopVoiceTests
{
    private static MidiNote Note(double start, double end, int pitch, int channel = 0) =>
        new(channel, pitch, start, end);

    private static int[] Pitches(IReadOnlyList<MidiNote> line) => [.. line.Select(note => note.Pitch)];

    [Fact]
    public void Empty_input_yields_empty_line()
    {
        Assert.Empty(TopVoice.Extract([]));
    }

    [Fact]
    public void Keeps_the_highest_note_of_one_time_point()
    {
        var line = TopVoice.Extract([Note(0, 1, 60), Note(0, 1, 72), Note(0, 1, 67)]);

        Assert.Equal([72], Pitches(line));
    }

    [Fact]
    public void Equal_pitch_keeps_the_longer_note()
    {
        var line = TopVoice.Extract([Note(0, 1, 60), Note(0, 3, 60)]);

        Assert.Equal(Note(0, 3, 60), Assert.Single(line));
    }

    [Fact]
    public void Output_is_ordered_by_time_point()
    {
        var line = TopVoice.Extract([Note(2, 3, 64), Note(0, 1, 60)]);

        Assert.Equal([60, 64], Pitches(line));
    }

    [Fact]
    public void Time_points_are_quarter_beats_wide()
    {
        var line = TopVoice.Extract([Note(0, 1, 60), Note(0.1, 1, 64), Note(0.25, 1, 62)]);

        Assert.Equal([64, 62], Pitches(line));
    }

    [Theory]
    [InlineData(0.125, 1)]
    [InlineData(0.375, 2)]
    public void Time_point_rounding_goes_to_the_even_side(double start, int expectedCount)
    {
        var line = TopVoice.Extract([Note(0, 1, 60), Note(start, 1, 64)]);

        Assert.Equal(expectedCount, line.Count);
    }

    [Fact]
    public void Drops_notes_more_than_an_octave_below_the_local_high_note()
    {
        var line = TopVoice.Extract([Note(0, 1, 72), Note(1, 2, 40), Note(2, 3, 71)]);

        Assert.Equal([72, 71], Pitches(line));
    }

    [Fact]
    public void Keeps_notes_an_octave_below_the_local_high_note()
    {
        var line = TopVoice.Extract([Note(0, 1, 72), Note(1, 2, 60)]);

        Assert.Equal([72, 60], Pitches(line));
    }

    [Fact]
    public void A_low_verse_survives_when_the_high_chorus_is_outside_the_window()
    {
        var line = TopVoice.Extract(
            [Note(0, 1, 50), Note(2, 3, 52), Note(4, 5, 50), Note(20, 21, 86), Note(22, 23, 84)]);

        Assert.Equal([50, 52, 50, 86, 84], Pitches(line));
    }

    [Fact]
    public void The_local_window_reaches_eight_beats()
    {
        var line = TopVoice.Extract([Note(0, 1, 50), Note(8, 9, 80)]);

        Assert.Equal([80], Pitches(line));
    }

    [Fact]
    public void Notes_beyond_eight_beats_do_not_set_the_local_high_note()
    {
        var line = TopVoice.Extract([Note(0, 1, 50), Note(8.25, 9, 80)]);

        Assert.Equal([50, 80], Pitches(line));
    }

    [Fact]
    public void The_window_measures_start_times_not_sounding_durations()
    {
        var line = TopVoice.Extract([Note(0, 9, 84), Note(1, 2, 40), Note(10, 11, 41)]);

        Assert.Equal([84, 41], Pitches(line));
    }
}
