using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class KeyMapTests
{
    [Fact]
    public void DefaultEntries_CoverDingAndRing()
    {
        var keyMap = new KeyMap(KeyMap.DefaultEntries);

        Assert.Equal(9, keyMap.MidiNotes.Count);
        Assert.True(keyMap.Contains(NoteNames.ToMidi("A3")));
        foreach (var note in KeyMap.RingNotes)
        {
            Assert.True(keyMap.Contains(NoteNames.ToMidi(note)), note);
        }
    }

    [Fact]
    public void Constructor_SkipsInvalidEntries()
    {
        var keyMap = new KeyMap(new[]
        {
            new KeyMapEntry("A3", "B"),
            new KeyMapEntry("JUNK", "X"),
            new KeyMapEntry("E4", ""),
            new KeyMapEntry("E4", "F"),
        });

        Assert.Equal(2, keyMap.MidiNotes.Count);
        Assert.True(keyMap.TryGet(NoteNames.ToMidi("A3"), out var key));
        Assert.Equal("B", key);
    }

    [Fact]
    public void SortedMidiNotes_Ascending()
    {
        var keyMap = new KeyMap(KeyMap.DefaultEntries);

        Assert.Equal(keyMap.MidiNotes.OrderBy(m => m), keyMap.SortedMidiNotes);
    }

    [Fact]
    public void TryGet_FailsForUnknownNote()
    {
        var keyMap = new KeyMap(KeyMap.DefaultEntries);

        Assert.False(keyMap.TryGet(NoteNames.ToMidi("C4"), out _));
    }
}
