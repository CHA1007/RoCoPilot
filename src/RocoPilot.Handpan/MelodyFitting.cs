namespace RocoPilot.Handpan;

public static class MelodyFitting
{
    private static readonly int[] FoldOffsets = [12, -12, 24, -24, 36, -36];
    private static readonly int[] SnapOctaves = [0, -12, 12];
    private static readonly int[] MajorScaleSemitones = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorScaleSemitones = [0, 2, 3, 5, 7, 8, 10];

    private const int TransposeRange = 11;
    private const int SnapRange = 2;
    private const int WideSnapRange = 3;
    private const double FoldWeight = 3;
    private const double SnapWeight = 30;
    private const double LeapWeight = 0.5;
    private const int LeapToleranceSemitones = 12;
    private const double CoverageTolerance = 1e-9;

    public static IReadOnlyList<MidiNote> Transpose(IReadOnlyList<MidiNote> notes, int semitones)
    {
        if (semitones == 0)
        {
            return notes;
        }

        return [.. notes.Select(note => note with { Pitch = Math.Clamp(note.Pitch + semitones, 0, 127) })];
    }

    public static IReadOnlyList<MidiNote> Spacing(IReadOnlyList<MidiNote> notes, double minIntervalBeats)
    {
        if (minIntervalBeats <= 0)
        {
            return notes;
        }

        var output = new List<MidiNote>(notes.Count);
        double? earliestNext = null;
        foreach (var group in MelodyLine.OnsetGroups(notes))
        {
            var onset = group.Min(note => note.StartBeat);
            var shift = earliestNext is { } earliest && onset < earliest ? earliest - onset : 0;
            earliestNext = onset + shift + minIntervalBeats;
            foreach (var note in group)
            {
                output.Add(note with
                {
                    StartBeat = note.StartBeat + shift,
                    EndBeat = note.EndBeat + shift,
                });
            }
        }

        return output;
    }

    public static MelodyFit Fit(
        IReadOnlyList<MidiNote> notes,
        KeyMap keyMap,
        MusicKey? key = null,
        int? transpose = null)
    {
        var ordered = MelodyLine.InTimeOrder(notes);
        var semitones = transpose ?? BestTransposition(ordered, keyMap, key);
        var (folded, foldedCount) = Fold(Transpose(ordered, semitones), keyMap);
        var (widened, snappedCount) = SnappedChain(folded, keyMap, key);
        return new MelodyFit(
            widened,
            semitones,
            ExactCoverage(folded, keyMap),
            foldedCount,
            snappedCount,
            Missing(widened, keyMap));
    }

    public static IReadOnlyList<MidiNote> Missing(IReadOnlyList<MidiNote> notes, KeyMap keyMap) =>
        [.. notes.Where(note => !keyMap.Contains(note.Pitch))];

    public static double ExactCoverage(IReadOnlyList<MidiNote> notes, KeyMap keyMap)
    {
        if (notes.Count == 0)
        {
            return 0;
        }

        var totalBeats = notes.Sum(Beats);
        if (totalBeats <= 0)
        {
            return notes.Count(note => keyMap.Contains(note.Pitch)) / (double)notes.Count;
        }

        return notes.Where(note => keyMap.Contains(note.Pitch)).Sum(Beats) / totalBeats;
    }

    private static double Beats(MidiNote note) => Math.Max(0, note.EndBeat - note.StartBeat);

    public static int BestTransposition(IReadOnlyList<MidiNote> notes, KeyMap keyMap, MusicKey? key = null)
    {
        var best = 0;
        var bestCoverage = -1.0;
        var bestCost = double.MaxValue;
        for (var semitones = -TransposeRange; semitones <= TransposeRange; semitones++)
        {
            var (folded, foldedCount) = Fold(Transpose(notes, semitones), keyMap);
            if (folded.Count == 0)
            {
                continue;
            }

            var coverage = ExactCoverage(folded, keyMap);
            var (widened, snappedCount) = SnappedChain(folded, keyMap, key);
            var cost = foldedCount * FoldWeight
                + snappedCount * SnapWeight
                + LeapBeats(widened) * LeapWeight
                + Math.Abs(semitones);
            if (coverage > bestCoverage + CoverageTolerance
                || (Math.Abs(coverage - bestCoverage) <= CoverageTolerance && cost < bestCost))
            {
                best = semitones;
                bestCoverage = coverage;
                bestCost = cost;
            }
        }

        return best;
    }

    private static double LeapBeats(IReadOnlyList<MidiNote> notes)
    {
        var leap = 0.0;
        int? previous = null;
        foreach (var note in notes)
        {
            if (previous is { } pitch)
            {
                var distance = Math.Abs(note.Pitch - pitch);
                if (distance > LeapToleranceSemitones)
                {
                    leap += (distance - LeapToleranceSemitones) * Beats(note);
                }
            }

            previous = note.Pitch;
        }

        return leap;
    }

    private static (IReadOnlyList<MidiNote> Notes, int Folded) Fold(IReadOnlyList<MidiNote> notes, KeyMap keyMap)
    {
        var output = new List<MidiNote>(notes.Count);
        var folded = 0;
        int? previous = null;
        foreach (var note in notes)
        {
            var pitch = note.Pitch;
            if (!keyMap.Contains(pitch))
            {
                var candidates = FoldOffsets
                    .Select(offset => pitch + offset)
                    .Where(keyMap.Contains)
                    .ToList();
                if (candidates.Count > 0)
                {
                    pitch = previous is { } last
                        ? candidates.MinBy(candidate => Math.Abs(candidate - last))
                        : candidates[0];
                    folded++;
                }
            }

            if (keyMap.Contains(pitch))
            {
                previous = pitch;
            }

            output.Add(pitch == note.Pitch ? note : note with { Pitch = pitch });
        }

        return (output, folded);
    }

    private static (IReadOnlyList<MidiNote> Notes, int Snapped) SnappedChain(
        IReadOnlyList<MidiNote> folded,
        KeyMap keyMap,
        MusicKey? key)
    {
        var (snapped, narrowCount) = Snap(folded, keyMap, SnapRange, key);
        var (widened, wideCount) = Snap(snapped, keyMap, WideSnapRange, key);
        return (widened, narrowCount + wideCount);
    }

    private static (IReadOnlyList<MidiNote> Notes, int Snapped) Snap(
        IReadOnlyList<MidiNote> notes,
        KeyMap keyMap,
        int range,
        MusicKey? key)
    {
        var available = keyMap.SortedMidiNotes;
        var output = new List<MidiNote>(notes.Count);
        var snapped = 0;
        foreach (var note in notes)
        {
            var pitch = note.Pitch;
            if (!keyMap.Contains(pitch) && SnapTarget(pitch, available, key, range) is { } target)
            {
                pitch = target;
                snapped++;
            }

            output.Add(pitch == note.Pitch ? note : note with { Pitch = pitch });
        }

        return (output, snapped);
    }

    private static int? SnapTarget(int pitch, IReadOnlyList<int> available, MusicKey? key, int range)
    {
        int? best = null;
        (int Distance, int OffScale, int FoldPenalty, int Candidate)? bestKey = null;
        foreach (var candidate in available)
        {
            var distance = SnapOctaves.Min(octave => Math.Abs(candidate + octave - pitch));
            if (distance > range)
            {
                continue;
            }

            var ranking = (
                distance,
                key is { } musicKey && !IsScaleTone(candidate, musicKey) ? 1 : 0,
                Math.Abs(candidate - pitch) == distance ? 0 : 1,
                candidate);
            if (bestKey is null || ranking.CompareTo(bestKey.Value) < 0)
            {
                best = candidate;
                bestKey = ranking;
            }
        }

        return best;
    }

    private static bool IsScaleTone(int pitch, MusicKey key)
    {
        if (!NoteNames.TryPitchClassOf(key.Tonic, out var tonicPitchClass))
        {
            return true;
        }

        var scale = key.IsMinor ? MinorScaleSemitones : MajorScaleSemitones;
        return scale.Contains(((pitch - tonicPitchClass) % 12 + 12) % 12);
    }
}
