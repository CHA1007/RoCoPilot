using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class HandpanFittingTests
{
    private static readonly KeyMap KeyMap = new(KeyMap.DefaultEntries);

    private static IReadOnlyList<HandpanEvent> Notes(params (int midi, double beats)[] notes) =>
        notes.Select(n => HandpanEvent.Note(n.midi, n.beats, "x", NoteNames.Of(n.midi))).ToList();

    [Fact]
    public void Transpose_ShiftsMidiAndExtras()
    {
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(60, 1, "1", "C4", [64]),
            HandpanEvent.Rest(1),
        };

        var shifted = HandpanFitting.Transpose(events, 4);

        Assert.Equal(64, shifted[0].Midi);
        Assert.Equal(68, shifted[0].Extras![0]);
        Assert.Equal(HandpanEventKind.Rest, shifted[1].Kind);
        Assert.Equal(events, HandpanFitting.Transpose(events, 0));
    }

    [Fact]
    public void Fold_MovesOutOfRangeNotesIntoRange()
    {
        var events = Notes((72, 1), (84, 1));

        var (folded, count) = HandpanFitting.Fold(events, KeyMap);

        Assert.Equal(1, count);
        Assert.True(KeyMap.Contains(folded[1].Midi));
        Assert.True(KeyMap.Contains(folded[0].Midi));
    }

    [Fact]
    public void Fold_KeepsInRangeNotesUntouched()
    {
        var events = Notes((NoteNames.ToMidi("E4"), 1));

        var (folded, count) = HandpanFitting.Fold(events, KeyMap);

        Assert.Equal(0, count);
        Assert.Equal(events, folded);
    }

    [Fact]
    public void Fold_PrefersNearestOctaveToPreviousNote()
    {
        var low = NoteNames.ToMidi("G4");
        var events = Notes((low, 1), (NoteNames.ToMidi("C4") + 24, 1));

        var (folded, _) = HandpanFitting.Fold(events, KeyMap);

        var candidate = folded[1].Midi;
        Assert.True(KeyMap.Contains(candidate));
        Assert.True(Math.Abs(candidate - low) <= 12);
    }

    [Fact]
    public void Fold_FoldsExtrasToo()
    {
        var inRange = NoteNames.ToMidi("E4");
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(inRange, 1, "x", "E4", [NoteNames.ToMidi("E6")]),
        };

        var (folded, count) = HandpanFitting.Fold(events, KeyMap);

        Assert.True(count > 0);
        Assert.Equal(inRange, folded[0].Midi);
        Assert.DoesNotContain(folded[0].Extras!, m => m == NoteNames.ToMidi("E6"));
    }

    [Fact]
    public void Snap_MovesClosestAvailableNote()
    {
        var gSharp4 = NoteNames.ToMidi("G#4");
        var events = Notes((gSharp4, 1));

        var (snapped, count) = HandpanFitting.Snap(events, KeyMap);

        Assert.Equal(1, count);
        Assert.Equal(NoteNames.ToMidi("G4"), snapped[0].Midi);
    }

    [Fact]
    public void Snap_LeavesPlayableNotesAlone()
    {
        var events = Notes((NoteNames.ToMidi("A4"), 1));

        var (snapped, count) = HandpanFitting.Snap(events, KeyMap);

        Assert.Equal(0, count);
        Assert.Equal(events, snapped);
    }

    [Fact]
    public void Snap_RespectsMaxSemitones()
    {
        var fSharp4 = NoteNames.ToMidi("F#4");
        var events = Notes((fSharp4, 1));

        var (_, strictCount) = HandpanFitting.Snap(events, KeyMap, maxSemitones: 0);
        Assert.Equal(0, strictCount);

        var (snapped, looseCount) = HandpanFitting.Snap(events, KeyMap, maxSemitones: 2);
        Assert.Equal(1, looseCount);
        Assert.Equal(NoteNames.ToMidi("F4"), snapped[0].Midi);
    }

    [Fact]
    public void Snap_DropsUnmappableExtras()
    {
        var e4 = NoteNames.ToMidi("E4");
        var fSharp4 = NoteNames.ToMidi("F#4");
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(e4, 1, "x", "E4", [fSharp4]),
        };

        var (snapped, _) = HandpanFitting.Snap(events, KeyMap, maxSemitones: 0);

        Assert.Equal(e4, snapped[0].Midi);
        Assert.Empty(snapped[0].Extras!);
    }

    [Fact]
    public void BestTransposition_FindsFullCoverage()
    {
        var cMajorFromLowC = Notes((60, 1), (62, 1), (64, 1), (65, 1), (67, 1));

        var (semitones, coverage) = HandpanFitting.BestTransposition(cMajorFromLowC, KeyMap);

        Assert.Equal(1.0, coverage, 5);
        Assert.Equal(0, semitones);
    }

    [Fact]
    public void Gap_ExtendsNoteBeats()
    {
        var events = Notes((60, 1));
        var gapped = HandpanFitting.Gap(events, 0.25);

        Assert.Equal(1.25, gapped[0].Beats);
        Assert.Equal(events, HandpanFitting.Gap(events, 0));
    }

    [Fact]
    public void DedupeExtras_RemovesDuplicates()
    {
        var e4 = NoteNames.ToMidi("E4");
        var g4 = NoteNames.ToMidi("G4");
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(e4, 1, "x", "E4", [e4, g4, g4]),
        };

        var deduped = HandpanFitting.DedupeExtras(events);

        Assert.Equal([g4], deduped[0].Extras!);
    }
}
