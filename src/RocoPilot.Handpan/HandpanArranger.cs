namespace RocoPilot.Handpan;

public sealed record ArrangementOptions(
    int Transpose = 0,
    double MinIntervalSeconds = 0,
    double BpmOverride = 0,
    double SpeedPercent = 100,
    bool Accompany = false,
    double AccompanyEveryBeats = 2,
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
        var selection = MelodyPicker.Pick(score);
        var fit = MelodyFitting.Fit(selection.Line, keyMap, meta.Key, chosen.Transpose);
        var fitted = chosen.Accompany
            ? WithAccompaniment(selection, fit, keyMap, meta.Key, chosen.AccompanyEveryBeats)
            : fit.Notes;
        var notes = MelodyFitting.Spacing(fitted, chosen.MinIntervalSeconds * meta.Bpm / 60.0);
        return new HandpanArrangement(
            meta,
            selection.Line,
            fit,
            notes,
            ChartBuilder.Build(notes, meta, keyMap, title),
            PlaybackPlanner.Plan(notes, meta.Bpm, keyMap, chosen.Timing));
    }

    private static IReadOnlyList<MidiNote> WithAccompaniment(
        MelodySelection selection,
        MelodyFit fit,
        KeyMap keyMap,
        MusicKey? key,
        double everyBeats)
    {
        var track = Accompaniment.PickTrack(selection);
        if (track is null)
        {
            return fit.Notes;
        }

        var line = Accompaniment.Extract(track, everyBeats);
        if (line.Count == 0)
        {
            return fit.Notes;
        }

        var accompaniment = MelodyFitting.Fit(line, keyMap, key, fit.Semitones);
        return [.. MelodyLine.InTimeOrder(
            [.. fit.Notes, .. accompaniment.Notes.Where(note => keyMap.Contains(note.Pitch))])];
    }
}
