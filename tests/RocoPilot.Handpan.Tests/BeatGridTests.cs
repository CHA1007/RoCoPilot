using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class BeatGridTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 48)]
    [InlineData(1.5, 72)]
    [InlineData(-0.5, -24)]
    public void Beats_convert_to_grid_units(double beats, long expected)
    {
        Assert.Equal(expected, BeatGrid.ToUnits(beats));
    }

    [Fact]
    public void A_half_unit_rounds_to_the_even_side()
    {
        Assert.Equal(0, BeatGrid.ToUnits(1.0 / 96));
        Assert.Equal(2, BeatGrid.ToUnits(3.0 / 96));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(48, 1)]
    [InlineData(72, 1.5)]
    public void Grid_units_convert_back_to_beats(long units, double expected)
    {
        Assert.Equal(expected, BeatGrid.ToBeats(units));
    }

}
