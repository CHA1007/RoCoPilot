namespace RocoPilot.Handpan;

public static class HandpanFitting
{
    private static readonly int[] FoldOffsets = [12, -12, 24, -24, 36, -36];

    public static IReadOnlyList<HandpanEvent> Transpose(IReadOnlyList<HandpanEvent> events, int semitones)
    {
        if (semitones == 0)
        {
            return events;
        }

        return events.Select(e => e.Kind != HandpanEventKind.Note
            ? e
            : e with
            {
                Midi = e.Midi + semitones,
                NoteName = NoteNames.Of(e.Midi + semitones),
                Extras = e.Extras?.Select(m => m + semitones).ToList(),
            }).ToList();
    }

    public static (IReadOnlyList<HandpanEvent> Events, int Folded) Fold(
        IReadOnlyList<HandpanEvent> events, KeyMap keyMap)
    {
        var output = new List<HandpanEvent>(events.Count);
        var folded = 0;
        int? previous = null;

        foreach (var e in events)
        {
            if (e.Kind != HandpanEventKind.Note)
            {
                output.Add(e);
                continue;
            }

            var current = e;
            if (!keyMap.Contains(e.Midi))
            {
                var candidates = FoldOffsets
                    .Select(off => e.Midi + off)
                    .Where(keyMap.Contains)
                    .ToList();
                if (candidates.Count > 0)
                {
                    var midi = previous is { } prev
                        ? candidates.MinBy(m => Math.Abs(m - prev))
                        : candidates[0];
                    current = e with { Midi = midi, NoteName = NoteNames.Of(midi) };
                    folded++;
                }
            }

            if (current.Extras is { Count: > 0 })
            {
                var foldedExtras = new List<int>();
                var anyFolded = false;
                foreach (var extra in current.Extras)
                {
                    var mapped = FoldOne(extra, keyMap);
                    anyFolded |= mapped.Folded;
                    if (mapped.Midi == current.Midi || foldedExtras.Contains(mapped.Midi))
                    {
                        continue;
                    }

                    foldedExtras.Add(mapped.Midi);
                }

                if (anyFolded)
                {
                    current = current with { Extras = foldedExtras };
                    folded++;
                }
            }

            if (keyMap.Contains(current.Midi))
            {
                previous = current.Midi;
            }

            output.Add(current);
        }

        return (output, folded);
    }

    public static (IReadOnlyList<HandpanEvent> Events, int Snapped) Snap(
        IReadOnlyList<HandpanEvent> events, KeyMap keyMap, int maxSemitones = 2, string? tonic = null)
    {
        var output = new List<HandpanEvent>(events.Count);
        var snapped = 0;
        var available = keyMap.SortedMidiNotes;
        int? tonicPc = tonic is { Length: > 0 } && NoteNames.TryToMidi(tonic + "4", out var tonicMidi)
            ? ((tonicMidi % 12) + 12) % 12
            : null;

        foreach (var e in events)
        {
            if (e.Kind != HandpanEventKind.Note)
            {
                output.Add(e);
                continue;
            }

            var current = e;
            if (!keyMap.Contains(e.Midi))
            {
                var best = SnapFind(e.Midi, available, tonicPc, maxSemitones);
                if (best is { } target)
                {
                    current = e with { Midi = target, NoteName = NoteNames.Of(target) };
                    snapped++;
                }
            }

            if (current.Extras is { Count: > 0 })
            {
                var snappedExtras = new List<int>();
                var changed = false;
                foreach (var extra in current.Extras)
                {
                    if (keyMap.Contains(extra))
                    {
                        if (extra != current.Midi && !snappedExtras.Contains(extra))
                        {
                            snappedExtras.Add(extra);
                        }

                        continue;
                    }

                    var best = SnapFind(extra, available, tonicPc, maxSemitones);
                    if (best is { } target && target != current.Midi && !snappedExtras.Contains(target))
                    {
                        snappedExtras.Add(target);
                        snapped++;
                    }

                    changed = true;
                }

                if (changed)
                {
                    current = current with { Extras = snappedExtras };
                }
            }

            output.Add(current);
        }

        return (output, snapped);
    }

    public static (int Semitones, double Coverage) BestTransposition(
        IReadOnlyList<HandpanEvent> events, KeyMap keyMap)
    {
        var best = 0;
        var bestCoverage = -1.0;
        var bestCost = double.MaxValue;

        for (var s = -11; s <= 11; s++)
        {
            var (foldedEvents, foldedCount) = Fold(Transpose(events, s), keyMap);
            var notes = foldedEvents.Where(e => e.Kind == HandpanEventKind.Note).ToList();
            if (notes.Count == 0)
            {
                continue;
            }

            var total = notes.Sum(e => e.Beats);
            var exact = notes.Where(e => keyMap.Contains(e.Midi)).Sum(e => e.Beats) / total;
            var snappedCount = Snap(foldedEvents, keyMap).Snapped;
            var leap = 0.0;
            int? previous = null;
            foreach (var e in notes)
            {
                if (previous is { } prev && Math.Abs(e.Midi - prev) > 12)
                {
                    leap += (Math.Abs(e.Midi - prev) - 12) * e.Beats;
                }

                previous = e.Midi;
            }

            var cost = foldedCount * 3 + snappedCount * 30 + leap * 0.5 + Math.Abs(s);
            if (exact > bestCoverage + 1e-9 || (Math.Abs(exact - bestCoverage) <= 1e-9 && cost < bestCost))
            {
                best = s;
                bestCoverage = exact;
                bestCost = cost;
            }
        }

        return (best, bestCoverage);
    }

    public static IReadOnlyList<HandpanEvent> Gap(IReadOnlyList<HandpanEvent> events, double gapBeats)
    {
        if (gapBeats <= 0)
        {
            return events;
        }

        return events.Select(e => e.Kind == HandpanEventKind.Note
            ? e with { Beats = e.Beats + gapBeats }
            : e).ToList();
    }

    public static IReadOnlyList<HandpanEvent> DedupeExtras(IReadOnlyList<HandpanEvent> events)
    {
        var output = new List<HandpanEvent>(events.Count);
        foreach (var e in events)
        {
            if (e.Kind == HandpanEventKind.Note && e.Extras is { Count: > 0 })
            {
                var extras = new List<int>();
                foreach (var extra in e.Extras)
                {
                    if (extra != e.Midi && !extras.Contains(extra))
                    {
                        extras.Add(extra);
                    }
                }

                if (extras.Count != e.Extras.Count)
                {
                    output.Add(e with { Extras = extras });
                    continue;
                }
            }

            output.Add(e);
        }

        return output;
    }

    private static (int Midi, bool Folded) FoldOne(int midi, KeyMap keyMap)
    {
        if (keyMap.Contains(midi))
        {
            return (midi, false);
        }

        foreach (var off in FoldOffsets)
        {
            if (keyMap.Contains(midi + off))
            {
                return (midi + off, true);
            }
        }

        return (midi, false);
    }

    private static int? SnapFind(int midi, IReadOnlyList<int> available, int? tonicPc, int maxSemitones)
    {
        int? best = null;
        (int D, int OffScale, int FoldPen, int Cand)? bestKey = null;
        foreach (var cand in available)
        {
            var d = new[] { 0, -12, 12 }.Min(k => Math.Abs(cand + k - midi));
            if (d > maxSemitones)
            {
                continue;
            }

            var offScale = 0;
            if (tonicPc is { } pc && !IsScaleTone(cand - pc))
            {
                offScale = 1;
            }

            var foldPen = Math.Abs(cand - midi) == d ? 0 : 1;
            var key = (d, offScale, foldPen, cand);
            if (bestKey is null || key.CompareTo((bestKey.Value.D, bestKey.Value.OffScale, bestKey.Value.FoldPen, bestKey.Value.Cand)) < 0)
            {
                best = cand;
                bestKey = key;
            }
        }

        return best;
    }

    private static bool IsScaleTone(int semitonesFromTonic)
    {
        var semi = ((semitonesFromTonic % 12) + 12) % 12;
        return semi is 0 or 2 or 4 or 5 or 7 or 9 or 11;
    }
}
