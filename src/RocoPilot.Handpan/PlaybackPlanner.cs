namespace RocoPilot.Handpan;

public sealed record PlannedNote(double AtSeconds, IReadOnlyList<string> Keys, double HoldSeconds);

public sealed record PlaybackPlan(
    double SecondsPerBeat,
    double TotalSeconds,
    IReadOnlyList<PlannedNote> Notes,
    int MissingCount);

public static class PlaybackPlanner
{
    public static PlaybackPlan Plan(
        IReadOnlyList<HandpanEvent> events,
        double bpm,
        KeyMap keyMap,
        double holdSeconds = 0.05,
        double chordHoldSeconds = 0.06)
    {
        var secondsPerBeat = 60.0 / bpm;
        var notes = new List<PlannedNote>();
        var missing = 0;
        var t = 0.0;

        foreach (var e in events)
        {
            if (e.Kind == HandpanEventKind.Note)
            {
                if (keyMap.Contains(e.Midi))
                {
                    var keys = new List<string>();
                    keyMap.TryGet(e.Midi, out var main);
                    keys.Add(main);
                    if (e.Extras is { Count: > 0 })
                    {
                        foreach (var extra in e.Extras)
                        {
                            if (keyMap.TryGet(extra, out var extraKey))
                            {
                                keys.Add(extraKey);
                            }
                        }
                    }

                    notes.Add(new PlannedNote(t, keys,
                        keys.Count > 1 ? chordHoldSeconds : holdSeconds));
                }
                else
                {
                    missing++;
                }
            }

            t += e.Beats * secondsPerBeat;
        }

        return new PlaybackPlan(secondsPerBeat, t, notes, missing);
    }
}
