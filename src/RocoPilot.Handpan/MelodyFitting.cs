namespace RocoPilot.Handpan;

public static class MelodyFitting
{
    private static readonly int[] FoldOffsets = [12, -12, 24, -24, 36, -36];
    private static readonly int[] SnapOctaves = [0, -12, 12];
    private static readonly int[] NaturalScaleSemitones = [0, 2, 4, 5, 7, 9, 11];

    private const int TransposeRange = 11;
    private const int SnapRange = 2;
    private const int WideSnapRange = 4;
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

    public static IReadOnlyList<MidiNote> Gap(IReadOnlyList<MidiNote> notes, double gapBeats)
    {
        if (gapBeats <= 0)
        {
            return notes;
        }

        var output = new List<MidiNote>(notes.Count);
        var onsets = -1;
        double? previous = null;
        foreach (var note in MelodyLine.InTimeOrder(notes))
        {
            if (previous is not { } start || !BeatGrid.SameOnset(start, note.StartBeat))
            {
                onsets++;
            }

            previous = note.StartBeat;
            var shift = onsets * gapBeats;
            output.Add(note with { StartBeat = note.StartBeat + shift, EndBeat = note.EndBeat + shift });
        }

        return output;
    }

    public static MelodyFit Fit(
        IReadOnlyList<MidiNote> notes,
        KeyMap keyMap,
        string? tonic = null,
        int? transpose = null)
    {
        var ordered = MelodyLine.InTimeOrder(notes);
        var semitones = transpose ?? BestTransposition(ordered, keyMap);
        var (folded, foldedCount) = Fold(Transpose(ordered, semitones), keyMap);
        var (snapped, snappedCount) = Snap(folded, keyMap, SnapRange, tonic);
        var (widened, widenedCount) = Snap(snapped, keyMap, WideSnapRange, tonic);
        return new MelodyFit(
            widened,
            semitones,
            ExactCoverage(folded, keyMap),
            foldedCount,
            snappedCount + widenedCount,
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

    public static int BestTransposition(IReadOnlyList<MidiNote> notes, KeyMap keyMap)
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
            var cost = foldedCount * FoldWeight
                + Snap(folded, keyMap, SnapRange, null).Snapped * SnapWeight
                + LeapBeats(folded) * LeapWeight
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

    private static (IReadOnlyList<MidiNote> Notes, int Snapped) Snap(
        IReadOnlyList<MidiNote> notes,
        KeyMap keyMap,
        int range,
        string? tonic)
    {
        var available = keyMap.SortedMidiNotes;
        int? tonicPitchClass = NoteNames.TryPitchClassOf(tonic, out var pitchClass) ? pitchClass : null;
        var output = new List<MidiNote>(notes.Count);
        var snapped = 0;
        foreach (var note in notes)
        {
            var pitch = note.Pitch;
            if (!keyMap.Contains(pitch) && SnapTarget(pitch, available, tonicPitchClass, range) is { } target)
            {
                pitch = target;
                snapped++;
            }

            output.Add(pitch == note.Pitch ? note : note with { Pitch = pitch });
        }

        return (output, snapped);
    }

    private static int? SnapTarget(int pitch, IReadOnlyList<int> available, int? tonicPitchClass, int range)
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

            var key = (
                distance,
                tonicPitchClass is { } tonic && !IsNaturalScaleTone(candidate - tonic) ? 1 : 0,
                Math.Abs(candidate - pitch) == distance ? 0 : 1,
                candidate);
            if (bestKey is null || key.CompareTo(bestKey.Value) < 0)
            {
                best = candidate;
                bestKey = key;
            }
        }

        return best;
    }

    private static bool IsNaturalScaleTone(int semitonesFromTonic) =>
        NaturalScaleSemitones.Contains(((semitonesFromTonic % 12) + 12) % 12);
}
