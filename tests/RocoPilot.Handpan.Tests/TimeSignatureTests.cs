using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class TimeSignatureTests
{
    [Theory]
    [InlineData(4, 4, 4)]
    [InlineData(3, 4, 3)]
    [InlineData(6, 8, 3)]
    [InlineData(2, 2, 4)]
    [InlineData(1, 1, 4)]
    [InlineData(12, 16, 3)]
    public void Beats_per_bar_follow_the_signature(int numerator, int denominator, double expected)
    {
        Assert.Equal(expected, new TimeSignature(numerator, denominator).BeatsPerBar);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(13, 4)]
    [InlineData(4, 3)]
    [InlineData(4, 0)]
    [InlineData(-4, 4)]
    public void An_unusable_signature_falls_back_to_four_beats(int numerator, int denominator)
    {
        Assert.Equal(4, new TimeSignature(numerator, denominator).BeatsPerBar);
    }
}
