namespace RocoPilot.Handpan;

public sealed record ArrangementOptions(
    int Transpose = 0,
    double MinIntervalSeconds = 0,
    double BpmOverride = 0,
    double SpeedPercent = 100,
    PlaybackTiming? Timing = null);

public sealed record HandpanArrangement(
    MidiMeta Meta,
    IReadOnlyList<MidiNote> Line,
    MelodyFit Fit,
    IReadOnlyList<MidiNote> Notes,
    HandpanChart Chart,
    PlaybackPlan Plan);

public static class HandpanArranger
{
    public static int BestTransposition(MidiScore score, KeyMap keyMap) =>
        MelodyFitting.BestTransposition(MelodyLine.InTimeOrder(MelodyPicker.Pick(score).Line), keyMap, score.Meta.Key);

    public static HandpanArrangement Arrange(
        MidiScore score,
        KeyMap keyMap,
        string? title = null,
        ArrangementOptions? options = null)
    {
        var chosen = options ?? new ArrangementOptions();
        var bpm = chosen.BpmOverride > 0
            ? chosen.BpmOverride
            : score.Meta.Bpm * chosen.SpeedPercent / 100.0;
        var meta = score.Meta with { Bpm = bpm };
        var line = MelodyPicker.Pick(score).Line;
        var fit = MelodyFitting.Fit(line, keyMap, meta.Key, chosen.Transpose);
        var notes = MelodyFitting.Spacing(fit.Notes, chosen.MinIntervalSeconds * meta.Bpm / 60.0);
        return new HandpanArrangement(
            meta,
            line,
            fit,
            notes,
            ChartBuilder.Build(notes, meta, keyMap, title),
            PlaybackPlanner.Plan(notes, meta.Bpm, keyMap, chosen.Timing));
    }
}
