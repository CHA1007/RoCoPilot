using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class MusicKeyTests
{
    [Theory]
    [InlineData("C", 0, "C")]
    [InlineData("C", 2, "D")]
    [InlineData("C", -1, "B")]
    [InlineData("A", 3, "C")]
    [InlineData("Bb", 2, "C")]
    [InlineData("F#", -11, "G")]
    public void Transposition_shifts_the_tonic(string tonic, int semitones, string expected)
    {
        Assert.Equal(new MusicKey(expected, false), new MusicKey(tonic, false).Transposed(semitones));
    }

    [Fact]
    public void Transposition_keeps_the_mode()
    {
        Assert.Equal(new MusicKey("C", true), new MusicKey("A", true).Transposed(3));
    }

    [Fact]
    public void A_full_octave_keeps_the_tonic()
    {
        var key = new MusicKey("G", true);

        Assert.Same(key, key.Transposed(12));
        Assert.Same(key, key.Transposed(-12));
    }

    [Fact]
    public void The_relative_major_names_the_shared_signature()
    {
        Assert.Equal("C", new MusicKey("A", true).RelativeMajor);
        Assert.Equal("A#", new MusicKey("G", true).RelativeMajor);
        Assert.Null(new MusicKey("C", false).RelativeMajor);
    }
}
