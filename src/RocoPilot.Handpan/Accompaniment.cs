namespace RocoPilot.Handpan;

public static class Accompaniment
{
    private const double BelowMelodySemitones = 1;

    public static PartProfile? PickTrack(MelodySelection selection)
    {
        PartProfile? best = null;
        foreach (var candidate in selection.Profiles)
        {
            if (candidate.PartIndex == selection.Part.PartIndex
                || candidate.AveragePitch >= selection.Part.AveragePitch - BelowMelodySemitones)
            {
                continue;
            }

            if (best is null
                || candidate.Polyphony > best.Polyphony
                || (candidate.Polyphony == best.Polyphony && candidate.NoteCount > best.NoteCount))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static IReadOnlyList<MidiNote> Extract(PartProfile track, double everyBeats)
    {
        var gridUnits = BeatGrid.ToUnits(everyBeats);
        if (gridUnits <= 0)
        {
            return [];
        }

        var stabs = new Dictionary<long, (int Low, int High)>();
        foreach (var group in MelodyLine.OnsetGroups(track.Notes))
        {
            var onset = BeatGrid.ToUnits(group[0].StartBeat);
            var target = (long)Math.Round((double)onset / gridUnits) * gridUnits;
            var low = group.Min(note => note.Pitch);
            var high = group.Max(note => note.Pitch);
            stabs[target] = stabs.TryGetValue(target, out var stab)
                ? (Math.Min(stab.Low, low), Math.Max(stab.High, high))
                : (low, high);
        }

        return [.. stabs
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => StabNotes(pair.Key, pair.Value, gridUnits))];
    }

    private static IEnumerable<MidiNote> StabNotes(long targetUnits, (int Low, int High) stab, long gridUnits)
    {
        var start = BeatGrid.ToBeats(targetUnits);
        var end = BeatGrid.ToBeats(targetUnits + gridUnits);
        yield return new MidiNote(0, stab.Low, start, end);
        if (stab.High != stab.Low)
        {
            yield return new MidiNote(0, stab.High, start, end);
        }
    }
}
