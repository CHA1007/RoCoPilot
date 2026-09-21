namespace RocoPilot.Handpan;

public static class MelodyLine
{
    private const int OnsetToleranceUnits = 2;

    public static IReadOnlyList<MidiNote> InTimeOrder(IReadOnlyList<MidiNote> notes) =>
        [.. notes.OrderBy(note => note.StartBeat).ThenByDescending(note => note.Pitch)];

    public static IReadOnlyList<IReadOnlyList<MidiNote>> OnsetGroups(IReadOnlyList<MidiNote> notes)
    {
        var groups = new List<List<MidiNote>>();
        foreach (var note in InTimeOrder(notes))
        {
            if (groups.Count > 0
                && Math.Abs(BeatGrid.ToUnits(note.StartBeat) - BeatGrid.ToUnits(groups[^1][0].StartBeat))
                    <= OnsetToleranceUnits)
            {
                groups[^1].Add(note);
                continue;
            }

            groups.Add([note]);
        }

        return [.. groups.Select(group =>
            (IReadOnlyList<MidiNote>)[.. group.OrderByDescending(note => note.Pitch)])];
    }

    public static double TotalBeats(IReadOnlyList<MidiNote> notes) =>
        notes.Count == 0 ? 0 : notes.Max(note => note.EndBeat);
}
