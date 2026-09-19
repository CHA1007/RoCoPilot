using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class ChartBuilderTests
{
    private static readonly KeyMap KeyMap = new(KeyMap.DefaultEntries);

    [Fact]
    public void Build_IncludesHeaderAndSequence()
    {
        var parsed = ScoreParser.Parse("""
            title=小星星
            1=C
            bpm=96
            1 1 5 5 | 6 6 5-
            """);
        var chart = ChartBuilder.Build(parsed.Events, parsed.Meta, KeyMap);

        Assert.Contains("《小星星》 手碟按键谱", chart.Text);
        Assert.Contains("调号 1=C  BPM=96", chart.Text);
        Assert.Contains("【演奏序列】", chart.Text);
        Assert.Contains("|  1|", chart.Text);
        Assert.Contains("[C4缺失](1)", chart.Text);
        Assert.Contains("H(2)", chart.Text);
        Assert.Equal(2, chart.MissingCount);
    }

    [Fact]
    public void Build_MarksMissingNotes()
    {
        var parsed = ScoreParser.Parse("1=C\n1 2");
        var chart = ChartBuilder.Build(parsed.Events, parsed.Meta, KeyMap);

        Assert.Equal(2, chart.MissingCount);
        Assert.Contains("[C4缺失]", chart.Text);
        Assert.Contains("← 游戏里没有这个音！", chart.Text);
    }

    [Fact]
    public void Build_RendersChordTokens()
    {
        var e4 = NoteNames.ToMidi("E4");
        var g4 = NoteNames.ToMidi("G4");
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(e4, 1, "x", "E4", [g4]),
        };

        var chart = ChartBuilder.Build(events, ScoreMeta.Default(), KeyMap);

        Assert.Contains("F+H(1)", chart.Text);
    }

    [Fact]
    public void TotalBeats_SumsEvents()
    {
        var events = new List<HandpanEvent>
        {
            HandpanEvent.Note(60, 1, "1", "C4"),
            HandpanEvent.Rest(2),
            HandpanEvent.Bar(),
        };

        Assert.Equal(3, ChartBuilder.TotalBeats(events));
    }
}
