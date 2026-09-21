using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class MelodyPickerTests
{
    private static MidiNote Note(double start, double end, int pitch, int channel = 0) =>
        new(channel, pitch, start, end);

    private static MidiPart Part(string name, params MidiNote[] notes) => new(name, notes);

    private static MidiScore Score(params MidiPart[] parts) =>
        new(parts, new MidiMeta(120, "C", new TimeSignature(4, 4)));

    private static MidiPart Monophonic(string name, int noteCount, int pitch) =>
        new(name, [.. Enumerable.Range(0, noteCount).Select(i => Note(i, i + 1, pitch))]);

    private static MidiPart Chordal(string name, int noteCount, int pitch) =>
        new(name, [.. Enumerable.Range(0, noteCount).Select(_ => Note(0, 10, pitch))]);

    private static MidiPart TenNotesWithOneOverlap() =>
        Part("边界",
            Note(0, 1, 60), Note(0.1, 1.1, 64), Note(2, 3, 60), Note(3, 4, 60), Note(4, 5, 60),
            Note(5, 6, 60), Note(6, 7, 60), Note(7, 8, 60), Note(8, 9, 60), Note(9, 10, 60));

    private static MidiPart ElevenNotesWithOneOverlap() =>
        Part("边界",
            Note(0, 1, 60), Note(0.1, 1.1, 64), Note(2, 3, 60), Note(3, 4, 60), Note(4, 5, 60),
            Note(5, 6, 60), Note(6, 7, 60), Note(7, 8, 60), Note(8, 9, 60), Note(9, 10, 60),
            Note(10, 11, 60));

    [Fact]
    public void A_score_without_parts_has_no_melody()
    {
        Assert.Throws<MelodyPickException>(() => MelodyPicker.Pick(Score()));
    }

    [Fact]
    public void A_drum_only_score_has_no_melody()
    {
        var score = Score(Part("鼓", Note(0, 1, 36, 9), Note(1, 2, 38, 9), Note(2, 3, 42, 9)));

        Assert.Throws<MelodyPickException>(() => MelodyPicker.Pick(score));
    }

    [Fact]
    public void Drum_notes_are_left_out_of_the_part_profile()
    {
        var profile = Assert.Single(MelodyPicker.Profiles(Score(
            Part("混合", Note(0, 1, 60), Note(1, 2, 64), Note(0, 1, 36, 9), Note(1, 2, 38, 9)))));

        Assert.Equal(2, profile.NoteCount);
        Assert.Equal(62, profile.AveragePitch);
        Assert.Equal(0, profile.Polyphony);
        Assert.Equal(1, profile.Monophony);
        Assert.All(profile.Notes, note => Assert.NotEqual(MidiChannels.Drums, note.Channel));
    }

    [Fact]
    public void Profiles_keep_the_score_part_index()
    {
        var profiles = MelodyPicker.Profiles(Score(
            Part("鼓", Note(0, 1, 36, 9), Note(1, 2, 36, 9), Note(2, 3, 36, 9)),
            Part("主旋律", Note(0, 1, 72), Note(1, 2, 74))));

        var profile = Assert.Single(profiles);
        Assert.Equal(1, profile.PartIndex);
        Assert.Equal("主旋律", profile.Name);
    }

    [Fact]
    public void Profiles_list_every_usable_part()
    {
        var profiles = MelodyPicker.Profiles(Score(Monophonic("一", 5, 72), Monophonic("二", 6, 60)));

        Assert.Equal([0, 1], profiles.Select(p => p.PartIndex));
        Assert.Equal([5, 6], profiles.Select(p => p.NoteCount));
    }

    [Fact]
    public void Profile_notes_are_ordered_by_start_beat_then_pitch()
    {
        var profile = Assert.Single(MelodyPicker.Profiles(Score(
            Part("乱序", Note(2, 3, 60), Note(0, 1, 67), Note(0, 1, 60), Note(1, 2, 64)))));

        Assert.Equal([0, 0, 1, 2], profile.Notes.Select(n => n.StartBeat));
        Assert.Equal([60, 67, 64, 60], profile.Notes.Select(n => n.Pitch));
    }

    [Fact]
    public void The_part_with_more_notes_wins_at_equal_register()
    {
        var selection = MelodyPicker.Pick(Score(Monophonic("少", 10, 72), Monophonic("多", 20, 72)));

        Assert.Equal("多", selection.Part.Name);
    }

    [Fact]
    public void A_high_part_wins_over_a_busy_low_part()
    {
        var selection = MelodyPicker.Pick(Score(Monophonic("贝斯", 200, 40), Monophonic("主旋律", 60, 72)));

        Assert.Equal("主旋律", selection.Part.Name);
    }

    [Fact]
    public void A_monophonic_part_wins_over_a_chordal_part_of_the_same_size()
    {
        var selection = MelodyPicker.Pick(Score(Chordal("和弦", 40, 64), Monophonic("主旋律", 40, 64)));

        Assert.Equal("主旋律", selection.Part.Name);
    }

    [Fact]
    public void Equal_scores_pick_the_earlier_part()
    {
        var selection = MelodyPicker.Pick(Score(Monophonic("前", 20, 72), Monophonic("后", 20, 72)));

        Assert.Equal("前", selection.Part.Name);
        Assert.Same(selection.Profiles[0], selection.Part);
    }

    [Theory]
    [InlineData(70, 9.0)]
    [InlineData(64, 4.2)]
    [InlineData(63, 3.1)]
    [InlineData(58, -0.9)]
    [InlineData(57, -2.05)]
    [InlineData(50, -7.65)]
    [InlineData(49, -8.72)]
    public void Melody_score_pins_the_register_weight_tiers(int pitch, double expected)
    {
        var profile = Assert.Single(MelodyPicker.Profiles(Score(Part("独音", Note(0, 1, pitch)))));

        Assert.Equal(expected, profile.MelodyScore, 9);
    }

    [Fact]
    public void Melody_score_penalizes_overlapping_notes()
    {
        var profile = Assert.Single(MelodyPicker.Profiles(Score(TenNotesWithOneOverlap())));

        Assert.Equal(0.1, profile.Polyphony, 9);
        Assert.Equal(60.4, profile.AveragePitch, 9);
        Assert.Equal(5.22, profile.MelodyScore, 9);
    }

    [Fact]
    public void A_monophonic_part_keeps_every_note_in_its_melody_line()
    {
        var selection = MelodyPicker.Pick(Score(ElevenNotesWithOneOverlap()));

        Assert.Equal(11, selection.Line.Count);
        Assert.Equal(selection.Part.Notes, selection.Line);
    }

    [Fact]
    public void A_polyphony_of_ten_percent_goes_through_top_voice_extraction()
    {
        var selection = MelodyPicker.Pick(Score(TenNotesWithOneOverlap()));

        Assert.Equal(0.1, selection.Part.Polyphony, 9);
        Assert.Equal(9, selection.Line.Count);
        Assert.Equal([64, 60, 60, 60, 60, 60, 60, 60, 60], selection.Line.Select(n => n.Pitch));
    }

    [Fact]
    public void The_melody_line_of_a_polyphonic_part_is_its_top_voice()
    {
        var selection = MelodyPicker.Pick(Score(Part("钢琴",
            Note(0, 1, 72), Note(1, 2, 74), Note(2, 3, 76), Note(3, 4, 72),
            Note(0, 2, 48), Note(2, 4, 43), Note(0, 1, 60), Note(2, 3, 64))));

        Assert.Equal(0.75, selection.Part.Polyphony, 9);
        Assert.Equal([72, 74, 76, 72], selection.Line.Select(n => n.Pitch));
    }
}
