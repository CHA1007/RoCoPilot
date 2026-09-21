namespace RocoPilot.Handpan;

public static class PlaybackPlanner
{
    public static PlaybackPlan Plan(
        IReadOnlyList<MidiNote> notes,
        double bpm,
        KeyMap keyMap,
        PlaybackTiming? timing = null)
    {
        var secondsPerBeat = 60 / bpm;
        var holds = timing ?? new PlaybackTiming();
        var planned = new List<PlannedNote>();
        var missing = 0;
        foreach (var group in MelodyLine.OnsetGroups(notes))
        {
            missing += group.Count(note => !keyMap.Contains(note.Pitch));
            var keys = Keys(group, keyMap);
            if (keys.Count == 0)
            {
                continue;
            }

            planned.Add(new PlannedNote(
                group[0].StartBeat * secondsPerBeat,
                keys,
                keys.Count > 1 ? Math.Max(0, holds.ChordHoldSeconds) : Math.Max(0, holds.HoldSeconds)));
        }

        var presses = ClampRepeatedKeys(Presses(planned, Math.Max(0, holds.ChordStaggerSeconds)));
        return new PlaybackPlan(planned, presses, KeyEvents(presses), secondsPerBeat, EndsAt(presses), missing);
    }

    private static IReadOnlyList<string> Keys(IReadOnlyList<MidiNote> group, KeyMap keyMap)
    {
        var keys = new List<string>(group.Count);
        foreach (var note in group)
        {
            if (keyMap.TryGet(note.Pitch, out var key) && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static List<KeyPress> Presses(IReadOnlyList<PlannedNote> notes, double stagger)
    {
        var presses = new List<KeyPress>();
        foreach (var note in notes)
        {
            var release = note.AtSeconds + stagger * (note.Keys.Count - 1) + note.HoldSeconds;
            for (var index = 0; index < note.Keys.Count; index++)
            {
                var down = note.AtSeconds + stagger * index;
                var hold = index == note.Keys.Count - 1 ? note.HoldSeconds : release - down;
                presses.Add(new KeyPress(note.Keys[index], down, hold));
            }
        }

        return presses;
    }

    private static IReadOnlyList<KeyPress> ClampRepeatedKeys(List<KeyPress> presses)
    {
        var ordered = presses
            .OrderBy(press => press.DownAtSeconds)
            .ThenBy(press => press.Key, StringComparer.Ordinal)
            .ToList();
        var previousOfKey = new Dictionary<string, int>();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (previousOfKey.TryGetValue(ordered[index].Key, out var previous))
            {
                var held = ordered[previous];
                ordered[previous] = held with
                {
                    HoldSeconds = Math.Max(0, Math.Min(held.HoldSeconds, ordered[index].DownAtSeconds - held.DownAtSeconds)),
                };
            }

            previousOfKey[ordered[index].Key] = index;
        }

        return ordered;
    }

    private static IReadOnlyList<KeyEvent> KeyEvents(IReadOnlyList<KeyPress> presses) =>
        [.. presses
            .SelectMany(press => new[]
            {
                new KeyEvent(press.Key, press.DownAtSeconds, true),
                new KeyEvent(press.Key, press.UpAtSeconds, false),
            })
            .OrderBy(keyEvent => keyEvent.AtSeconds)
            .ThenBy(keyEvent => keyEvent.IsDown)
            .ThenBy(keyEvent => keyEvent.Key, StringComparer.Ordinal)];

    private static double EndsAt(IReadOnlyList<KeyPress> presses) =>
        presses.Count == 0 ? 0 : presses.Max(press => press.UpAtSeconds);
}
