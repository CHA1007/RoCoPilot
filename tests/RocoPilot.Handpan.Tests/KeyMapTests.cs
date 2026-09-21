using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class KeyMapTests
{
    [Fact]
    public void Default_layout_maps_ding_and_ring_notes()
    {
        var map = new KeyMap(KeyMap.DefaultEntries);

        Assert.Equal(9, map.MidiNotes.Count);
        Assert.True(map.TryGet(57, out var ding) && ding == "B");
        Assert.True(map.TryGet(64, out var e4) && e4 == "F");
        Assert.True(map.TryGet(76, out var e5) && e5 == "U");
        Assert.Equal(new[] { 57, 64, 65, 67, 69, 71, 72, 74, 76 }, map.SortedMidiNotes);
        Assert.Empty(map.InvalidEntries);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("f1")]
    [InlineData(",")]
    [InlineData("comma")]
    [InlineData("enter")]
    [InlineData("space")]
    public void Accepts_multi_char_and_non_letter_keys(string key)
    {
        var map = new KeyMap([new KeyMapEntry("E4", key)]);

        Assert.True(map.TryGet(64, out var mapped));
        Assert.Equal(key.Trim(), mapped);
        Assert.Empty(map.InvalidEntries);
    }

    [Fact]
    public void Notes_are_case_insensitive()
    {
        var map = new KeyMap([new KeyMapEntry("e4", " F ")]);

        Assert.True(map.TryGet(64, out var key));
        Assert.Equal("F", key);
    }

    [Fact]
    public void Later_entry_for_same_note_wins()
    {
        var map = new KeyMap(
        [
            new KeyMapEntry("E4", "F"),
            new KeyMapEntry("E4", "G"),
        ]);

        Assert.True(map.TryGet(64, out var key));
        Assert.Equal("G", key);
    }

    [Theory]
    [InlineData("H4")]
    [InlineData("")]
    [InlineData("garbage")]
    public void Invalid_notes_are_rejected_and_recorded(string note)
    {
        var entry = new KeyMapEntry(note, "F");
        var map = new KeyMap([entry]);

        Assert.False(map.Contains(64));
        Assert.Equal([entry], map.InvalidEntries);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData("  ")]
    public void Unknown_key_names_are_rejected_and_recorded(string key)
    {
        var entry = new KeyMapEntry("E4", key);
        var map = new KeyMap([entry]);

        Assert.False(map.Contains(64));
        Assert.Equal([entry], map.InvalidEntries);
    }

    [Fact]
    public void Empty_entries_yield_empty_map()
    {
        var map = new KeyMap([]);

        Assert.Empty(map.MidiNotes);
        Assert.Empty(map.SortedMidiNotes);
        Assert.Empty(map.InvalidEntries);
        Assert.False(map.TryGet(64, out _));
    }
}
