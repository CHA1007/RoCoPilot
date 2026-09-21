using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class HandpanArrangerTests
{
    private static readonly KeyMap DefaultMap = new(KeyMap.DefaultEntries);

    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    private static MidiScore Score(
        double bpm = 120,
        MusicKey? key = null,
        params MidiNote[] notes) =>
        new([new MidiPart("主旋律", notes)], new MidiMeta(bpm, key, new TimeSignature(4, 4)));

    [Fact]
    public void A_line_inside_the_key_range_is_planned_as_is()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 72), Note(1, 2, 74)]),
            DefaultMap);

        Assert.Equal(0, arrangement.Fit.Semitones);
        Assert.Equal([72, 74], arrangement.Fit.Notes.Select(note => note.Pitch));
        Assert.Equal(2, arrangement.Plan.NoteCount);
        Assert.Equal(0, arrangement.Plan.MissingCount);
    }

    [Fact]
    public void Best_transposition_finds_the_shift_for_a_line_outside_the_key_range()
    {
        var score = Score(notes: [Note(0, 1, 62), Note(1, 2, 65), Note(2, 3, 67)]);

        Assert.Equal(2, HandpanArranger.BestTransposition(score, DefaultMap));
    }

    [Fact]
    public void The_computed_transposition_reaches_the_arrangement()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 62), Note(1, 2, 65), Note(2, 3, 67)]),
            DefaultMap,
            options: new ArrangementOptions(Transpose: 2));

        Assert.Equal(2, arrangement.Fit.Semitones);
        Assert.Equal([64, 67, 69], arrangement.Notes.Select(note => note.Pitch));
    }

    [Fact]
    public void An_explicit_transposition_is_applied_as_given()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 72)]),
            DefaultMap,
            options: new ArrangementOptions(Transpose: -5));

        Assert.Equal(-5, arrangement.Fit.Semitones);
        Assert.Equal([67], arrangement.Notes.Select(note => note.Pitch));
    }

    [Fact]
    public void Best_transposition_keeps_zero_for_a_line_already_in_range()
    {
        var score = Score(notes: [Note(0, 1, 72), Note(1, 2, 74)]);

        Assert.Equal(0, HandpanArranger.BestTransposition(score, DefaultMap));
    }

    [Fact]
    public void A_minor_key_snaps_chromatic_notes_to_the_minor_scale()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(key: new MusicKey("A", true), notes: [Note(0, 1, 73)]),
            DefaultMap);

        Assert.Equal([72], arrangement.Notes.Select(note => note.Pitch));
        Assert.StartsWith("调号 1=C（A 小调）", arrangement.Chart.Lines[1]);
    }

    [Fact]
    public void Best_transposition_of_a_score_without_a_melody_fails()
    {
        Assert.Throws<MelodyPickException>(() => HandpanArranger.BestTransposition(Score(), DefaultMap));
    }

    [Fact]
    public void A_dense_line_is_spaced_in_seconds()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 0.1, 72), Note(0.1, 0.2, 74), Note(0.2, 0.3, 76)]),
            DefaultMap,
            options: new ArrangementOptions(MinIntervalSeconds: 0.15));

        Assert.Equal([0, 0.3, 0.6], arrangement.Notes.Select(note => note.StartBeat));
        Assert.Equal([0, 0.15, 0.3], arrangement.Plan.Notes.Select(note => note.AtSeconds));
    }

    [Fact]
    public void A_sparse_line_is_left_alone_by_the_interval()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 72), Note(2, 3, 74)]),
            DefaultMap,
            options: new ArrangementOptions(MinIntervalSeconds: 0.15));

        Assert.Equal([0, 2], arrangement.Notes.Select(note => note.StartBeat));
    }

    [Fact]
    public void A_speed_percent_scales_the_score_tempo()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(bpm: 90, notes: [Note(0, 1, 72)]),
            DefaultMap,
            options: new ArrangementOptions(SpeedPercent: 50));

        Assert.Equal(45, arrangement.Meta.Bpm);
        Assert.Equal(60.0 / 45, arrangement.Plan.SecondsPerBeat);
    }

    [Fact]
    public void An_absolute_override_wins_over_the_percent()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(bpm: 90, notes: [Note(0, 1, 72)]),
            DefaultMap,
            options: new ArrangementOptions(BpmOverride: 60, SpeedPercent: 50));

        Assert.Equal(60, arrangement.Meta.Bpm);
    }

    [Fact]
    public void A_tempo_override_rescales_the_timeline_and_the_header()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(bpm: 120, notes: [Note(0, 1, 72), Note(2, 3, 74)]),
            DefaultMap,
            options: new ArrangementOptions(BpmOverride: 60));

        Assert.Equal(60, arrangement.Meta.Bpm);
        Assert.Equal(1, arrangement.Plan.SecondsPerBeat);
        Assert.Equal(2, arrangement.Plan.Notes[1].AtSeconds);
        Assert.Contains("BPM=60", arrangement.Chart.Lines[1]);
    }

    [Fact]
    public void The_score_tempo_is_kept_without_an_override()
    {
        var arrangement = HandpanArranger.Arrange(Score(bpm: 90, notes: [Note(0, 1, 72)]), DefaultMap);

        Assert.Equal(90, arrangement.Meta.Bpm);
        Assert.Equal(60.0 / 90, arrangement.Plan.SecondsPerBeat);
    }

    [Fact]
    public void Timing_options_reach_the_plan()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 72)]),
            DefaultMap,
            options: new ArrangementOptions(Timing: new PlaybackTiming(HoldSeconds: 0.2)));

        Assert.Equal(0.2, Assert.Single(arrangement.Plan.Notes).HoldSeconds);
    }

    [Fact]
    public void The_title_reaches_the_chart()
    {
        var arrangement = HandpanArranger.Arrange(Score(notes: [Note(0, 1, 72)]), DefaultMap, title: "晴天");

        Assert.Equal("《晴天》 手碟按键谱", arrangement.Chart.Lines[0]);
    }

    [Fact]
    public void The_chart_and_the_plan_share_one_timeline()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(bpm: 60, notes: [Note(0, 1, 72), Note(4, 5, 74)]),
            DefaultMap);

        Assert.Equal(5, arrangement.Chart.TotalBeats);
        Assert.Equal(4.05, arrangement.Plan.EndsAtSeconds, 9);
    }

    [Fact]
    public void A_score_without_a_melody_has_no_arrangement()
    {
        Assert.Throws<MelodyPickException>(() => HandpanArranger.Arrange(Score(), DefaultMap));
    }

    [Fact]
    public void The_picked_line_is_kept_before_fitting()
    {
        var arrangement = HandpanArranger.Arrange(
            Score(notes: [Note(0, 1, 62), Note(1, 2, 65)]),
            DefaultMap);

        Assert.Equal([62, 65], arrangement.Line.Select(note => note.Pitch));
        Assert.Equal([74, 65], arrangement.Notes.Select(note => note.Pitch));
    }
}
