using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class NoteNamesTests
{
    [Theory]
    [InlineData("C4", 60)]
    [InlineData("A3", 57)]
    [InlineData("c4", 60)]
    [InlineData("C#4", 61)]
    [InlineData("Db4", 61)]
    [InlineData("C-1", 0)]
    [InlineData("E5", 76)]
    [InlineData("B4", 71)]
    public void ToMidi_ParsesNames(string name, int midi)
    {
        Assert.Equal(midi, NoteNames.ToMidi(name));
    }

    [Fact]
    public void ToMidi_RejectsInvalidName()
    {
        Assert.Throws<FormatException>(() => NoteNames.ToMidi("H4"));
        Assert.Throws<FormatException>(() => NoteNames.ToMidi("4"));
    }

    [Fact]
    public void TryToMidi_ReportsFailure()
    {
        Assert.False(NoteNames.TryToMidi("xx", out _));
        Assert.True(NoteNames.TryToMidi("A3", out var midi));
        Assert.Equal(57, midi);
    }

    [Theory]
    [InlineData(60, "中音1")]
    [InlineData(64, "中音3")]
    [InlineData(57, "低音6")]
    [InlineData(72, "高音1")]
    [InlineData(73, null)]
    public void DegreeZh_MapsSemitones(int midi, string? expected)
    {
        Assert.Equal(expected, NoteNames.DegreeZh(midi));
    }

    [Fact]
    public void Of_RoundTrips()
    {
        Assert.Equal("A3", NoteNames.Of(NoteNames.ToMidi("A3")));
    }
}
