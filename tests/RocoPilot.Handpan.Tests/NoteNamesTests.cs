using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class NoteNamesTests
{
    [Theory]
    [InlineData(60, "C4")]
    [InlineData(61, "C#4")]
    [InlineData(64, "E4")]
    [InlineData(69, "A4")]
    [InlineData(71, "B4")]
    [InlineData(72, "C5")]
    [InlineData(57, "A3")]
    [InlineData(21, "A0")]
    [InlineData(108, "C8")]
    [InlineData(0, "C-1")]
    public void Of_returns_scientific_pitch_name(int midi, string expected)
    {
        Assert.Equal(expected, NoteNames.Of(midi));
    }

    [Theory]
    [InlineData("C4", 60)]
    [InlineData("c4", 60)]
    [InlineData(" E4 ", 64)]
    [InlineData("F#4", 66)]
    [InlineData("Gb4", 66)]
    [InlineData("Bb3", 58)]
    [InlineData("A#3", 58)]
    [InlineData("A3", 57)]
    [InlineData("C-1", 0)]
    public void TryToMidi_parses_names_case_insensitive(string name, int expected)
    {
        Assert.True(NoteNames.TryToMidi(name, out var midi));
        Assert.Equal(expected, midi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("H4")]
    [InlineData("C")]
    [InlineData("4")]
    [InlineData("C##4")]
    [InlineData("Cb4")]
    [InlineData("C10")]
    [InlineData("C4x")]
    public void TryToMidi_rejects_malformed_names(string name)
    {
        Assert.False(NoteNames.TryToMidi(name, out _));
    }

    [Fact]
    public void Name_round_trips_through_midi()
    {
        for (var midi = 0; midi <= 127; midi++)
        {
            var name = NoteNames.Of(midi);
            Assert.True(NoteNames.TryToMidi(name, out var back));
            Assert.Equal(midi, back);
        }
    }
}
