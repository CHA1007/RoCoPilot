namespace RocoPilot.Handpan;

public static class TopVoice
{
    private const int TimePointsPerBeat = 4;
    private const double LocalHighWindowBeats = 8;
    private const int LocalHighToleranceSemitones = 12;

    public static IReadOnlyList<MidiNote> Extract(IReadOnlyList<MidiNote> notes)
    {
        var line = HighestNotePerTimePoint(notes);
        var melody = new List<MidiNote>(line.Count);
        var windowStart = 0;
        for (var index = 0; index < line.Count; index++)
        {
            while (line[index].StartBeat - line[windowStart].StartBeat > LocalHighWindowBeats)
            {
                windowStart++;
            }

            var windowEnd = index;
            while (windowEnd + 1 < line.Count
                && line[windowEnd + 1].StartBeat - line[index].StartBeat <= LocalHighWindowBeats)
            {
                windowEnd++;
            }

            var localHigh = int.MinValue;
            for (var window = windowStart; window <= windowEnd; window++)
            {
                localHigh = Math.Max(localHigh, line[window].Pitch);
            }

            if (line[index].Pitch >= localHigh - LocalHighToleranceSemitones)
            {
                melody.Add(line[index]);
            }
        }

        return melody;
    }

    private static List<MidiNote> HighestNotePerTimePoint(IReadOnlyList<MidiNote> notes)
    {
        var byTimePoint = new SortedDictionary<long, MidiNote>();
        foreach (var note in notes)
        {
            var timePoint = (long)Math.Round(note.StartBeat * TimePointsPerBeat);
            if (!byTimePoint.TryGetValue(timePoint, out var current) || Outranks(note, current))
            {
                byTimePoint[timePoint] = note;
            }
        }

        return [.. byTimePoint.Values];
    }

    private static bool Outranks(MidiNote candidate, MidiNote current) =>
        candidate.Pitch > current.Pitch
        || (candidate.Pitch == current.Pitch && candidate.EndBeat > current.EndBeat);
}
