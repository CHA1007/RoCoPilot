namespace RocoPilot.Handpan;

public sealed record ProbeKey(string Key, string Note, double DownAtSeconds, double UpAtSeconds);

public static class ProbePlanner
{
    public const double IntervalSeconds = 0.9;
    public const double PressSeconds = 0.12;

    public static IReadOnlyList<ProbeKey> Plan(KeyMap keyMap)
    {
        var midiNotes = keyMap.SortedMidiNotes;
        var plan = new List<ProbeKey>(midiNotes.Count);
        for (var index = 0; index < midiNotes.Count; index++)
        {
            var midi = midiNotes[index];
            if (!keyMap.TryGet(midi, out var key))
            {
                continue;
            }

            var downAt = index * IntervalSeconds;
            plan.Add(new ProbeKey(key, NoteNames.Of(midi), downAt, downAt + PressSeconds));
        }

        return plan;
    }
}
