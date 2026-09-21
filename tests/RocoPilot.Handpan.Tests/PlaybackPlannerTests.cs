using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class PlaybackPlannerTests
{
    private static readonly KeyMap DefaultMap = new(KeyMap.DefaultEntries);

    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    private static PlaybackPlan Plan(
        IReadOnlyList<MidiNote> notes,
        KeyMap? keyMap = null,
        double bpm = 120,
        PlaybackTiming? timing = null) =>
        PlaybackPlanner.Plan(notes, bpm, keyMap ?? DefaultMap, timing);

    private static void AssertTimes(IEnumerable<double> expected, IEnumerable<double> actual)
    {
        Assert.Equal(expected.Count(), actual.Count());
        foreach (var (want, got) in expected.Zip(actual))
        {
            Assert.Equal(want, got, 9);
        }
    }

    [Fact]
    public void An_empty_line_plays_nothing()
    {
        var plan = Plan([]);

        Assert.Equal(0, plan.NoteCount);
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Presses);
        Assert.Equal(0, plan.EndsAtSeconds);
        Assert.Equal(0, plan.MissingCount);
        Assert.Equal(0.5, plan.SecondsPerBeat);
    }

    [Fact]
    public void A_note_is_planned_at_its_beat_time()
    {
        var plan = Plan([Note(2, 3, 72)]);

        var note = Assert.Single(plan.Notes);
        Assert.Equal(1, note.AtSeconds);
        Assert.Equal(["T"], note.Keys);
        Assert.Equal(0.05, note.HoldSeconds);
        Assert.False(note.IsChord);
    }

    [Fact]
    public void A_press_carries_its_key_and_hold()
    {
        var plan = Plan([Note(2, 3, 72)]);

        Assert.Equal(new KeyPress("T", 1, 0.05), Assert.Single(plan.Presses));
        Assert.Equal(1.05, Assert.Single(plan.Presses).UpAtSeconds);
    }

    [Fact]
    public void The_plan_ends_with_the_last_release()
    {
        Assert.Equal(0.55, Plan([Note(1, 2, 72)]).EndsAtSeconds, 10);
    }

    [Fact]
    public void The_beat_length_follows_the_tempo()
    {
        Assert.Equal(60.0 / 154, Plan([], bpm: 154).SecondsPerBeat);
    }

    [Fact]
    public void Notes_are_planned_in_time_order()
    {
        var plan = Plan([Note(2, 3, 74), Note(0, 1, 72)]);

        Assert.Equal([0, 1], plan.Notes.Select(note => note.AtSeconds));
    }

    [Fact]
    public void A_long_chord_does_not_push_later_notes()
    {
        var plan = Plan([Note(0, 4, 72), Note(0, 4, 76), Note(4, 5, 74)]);

        Assert.Equal([0, 2], plan.Notes.Select(note => note.AtSeconds));
        Assert.Equal(["U", "T"], plan.Notes[0].Keys);
        Assert.Equal(0.06, plan.Notes[0].HoldSeconds);
        Assert.Equal(0.05, plan.Notes[1].HoldSeconds);
    }

    [Fact]
    public void Overlapping_holds_on_different_keys_are_kept_whole()
    {
        var plan = Plan([Note(0, 1, 72), Note(0.02, 1, 74)]);

        Assert.Equal([0.05, 0.05], plan.Presses.Select(press => press.HoldSeconds));
        Assert.Equal(0.06, plan.EndsAtSeconds, 10);
    }

    [Fact]
    public void A_chord_staggers_its_keys_and_releases_them_together()
    {
        var plan = Plan([Note(0, 1, 72), Note(0, 1, 76)]);

        var note = Assert.Single(plan.Notes);
        Assert.Equal(["U", "T"], note.Keys);
        Assert.Equal(0.06, note.HoldSeconds);
        Assert.True(note.IsChord);
        Assert.Equal(["U", "T"], plan.Presses.Select(press => press.Key));
        AssertTimes([0, 0.012], plan.Presses.Select(press => press.DownAtSeconds));
        AssertTimes([0.072, 0.06], plan.Presses.Select(press => press.HoldSeconds));
        Assert.Equal(0.072, plan.EndsAtSeconds, 10);
    }

    [Fact]
    public void The_stagger_is_configurable()
    {
        var plan = Plan(
            [Note(0, 1, 72), Note(0, 1, 76)],
            timing: new PlaybackTiming(ChordStaggerSeconds: 0.02));

        Assert.Equal(["U", "T"], plan.Presses.Select(press => press.Key));
        AssertTimes([0, 0.02], plan.Presses.Select(press => press.DownAtSeconds));
        AssertTimes([0.08, 0.06], plan.Presses.Select(press => press.HoldSeconds));
    }

    [Fact]
    public void The_measured_constants_are_the_defaults()
    {
        Assert.Equal(new PlaybackTiming(0.05, 0.06, 0.012), new PlaybackTiming());
    }

    [Fact]
    public void Negative_timing_values_press_without_hold_or_stagger()
    {
        var plan = Plan(
            [Note(0, 1, 72), Note(0, 1, 76), Note(2, 3, 74)],
            timing: new PlaybackTiming(-1, -2, -3));

        Assert.Equal([0, 0], plan.Notes.Select(note => note.HoldSeconds));
        Assert.Equal(
            [new KeyPress("T", 0, 0), new KeyPress("U", 0, 0), new KeyPress("Y", 1, 0)],
            plan.Presses);
        Assert.Equal(1, plan.EndsAtSeconds);
    }

    [Fact]
    public void Presses_are_ordered_by_time_then_by_key()
    {
        var plan = Plan([Note(0, 1, 76), Note(0, 1, 72), Note(0.021, 1, 65)]);

        Assert.Equal(["U", "G", "T"], plan.Presses.Select(press => press.Key));
        AssertTimes([0, 0.0105, 0.012], plan.Presses.Select(press => press.DownAtSeconds));
    }

    [Fact]
    public void The_same_key_pressed_again_shortens_the_previous_hold()
    {
        var plan = Plan([Note(0, 1, 72), Note(0.04, 1, 72)]);

        AssertTimes([0, 0.02], plan.Presses.Select(press => press.DownAtSeconds));
        AssertTimes([0.02, 0.05], plan.Presses.Select(press => press.HoldSeconds));
        Assert.Equal(0.07, plan.EndsAtSeconds, 10);
    }

    [Fact]
    public void Holds_never_shift_later_notes()
    {
        var line = Enumerable.Range(0, 10).Select(index => Note(index * 0.04, index * 0.04 + 1, 72)).ToList();

        var plan = Plan(line);

        AssertTimes([0.18], plan.Notes.Select(note => note.AtSeconds).TakeLast(1));
        AssertTimes(
            [.. Enumerable.Repeat(0.02, 9).Append(0.05)],
            plan.Presses.Select(press => press.HoldSeconds));
        Assert.Equal(0.23, plan.EndsAtSeconds, 9);
    }

    [Fact]
    public void A_note_produces_one_down_and_one_up_event()
    {
        var plan = Plan([Note(1, 2, 72)]);

        Assert.Equal(
            [new KeyEvent("T", 0.5, true), new KeyEvent("T", 0.55, false)],
            plan.KeyEvents);
    }

    [Fact]
    public void A_chord_releases_every_key_at_the_same_instant()
    {
        var plan = Plan([Note(0, 1, 72), Note(0, 1, 76)]);

        Assert.Equal(["U", "T", "T", "U"], plan.KeyEvents.Select(keyEvent => keyEvent.Key));
        Assert.Equal([true, true, false, false], plan.KeyEvents.Select(keyEvent => keyEvent.IsDown));
        AssertTimes([0, 0.012, 0.072, 0.072], plan.KeyEvents.Select(keyEvent => keyEvent.AtSeconds));
    }

    [Fact]
    public void A_release_comes_before_a_repress_of_the_same_key()
    {
        var plan = Plan([Note(0, 1, 72), Note(0.04, 1, 72)]);

        Assert.Equal([true, false, true, false], plan.KeyEvents.Select(keyEvent => keyEvent.IsDown));
        AssertTimes([0, 0.02, 0.02, 0.07], plan.KeyEvents.Select(keyEvent => keyEvent.AtSeconds));
    }

    [Fact]
    public void An_unplayable_line_has_no_key_events()
    {
        Assert.Empty(Plan([Note(0, 1, 73)]).KeyEvents);
    }

    [Fact]
    public void A_chord_presses_one_key_per_distinct_mapping()
    {
        var shared = new KeyMap([new KeyMapEntry("C5", "T"), new KeyMapEntry("C6", "T")]);

        var plan = Plan([Note(0, 1, 72), Note(0, 1, 84)], shared);

        var note = Assert.Single(plan.Notes);
        Assert.Equal(["T"], note.Keys);
        Assert.Equal(0.05, note.HoldSeconds);
        Assert.Equal(new KeyPress("T", 0, 0.05), Assert.Single(plan.Presses));
    }

    [Fact]
    public void A_note_the_handpan_lacks_is_skipped_and_counted()
    {
        var plan = Plan([Note(0, 1, 73), Note(1, 2, 72)]);

        Assert.Equal(1, plan.MissingCount);
        Assert.Equal(0.5, Assert.Single(plan.Notes).AtSeconds);
        Assert.Equal(new KeyPress("T", 0.5, 0.05), Assert.Single(plan.Presses));
    }

    [Fact]
    public void A_chord_plays_only_the_keys_it_has()
    {
        var plan = Plan([Note(0, 1, 72), Note(0, 1, 73)]);

        var note = Assert.Single(plan.Notes);
        Assert.Equal(["T"], note.Keys);
        Assert.Equal(0.05, note.HoldSeconds);
        Assert.Equal(1, plan.MissingCount);
    }

    [Fact]
    public void A_wholly_unplayable_line_has_no_presses()
    {
        var plan = Plan([Note(0, 1, 30), Note(1, 2, 31)]);

        Assert.Equal(2, plan.MissingCount);
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Presses);
        Assert.Equal(0, plan.EndsAtSeconds);
    }
}
