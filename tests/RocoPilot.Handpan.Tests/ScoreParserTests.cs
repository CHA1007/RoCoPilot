using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class ScoreParserTests
{
    private static readonly KeyMap KeyMap = new(KeyMap.DefaultEntries);

    [Fact]
    public void Parse_ReadsHeadersAndNotes()
    {
        var parsed = ScoreParser.Parse("""
            title=小星星
            1=C
            bpm=96
            1 1 5 5 6 6 5-
            4 4 3 3 2 2 1-
            """);

        Assert.Equal("小星星", parsed.Meta.Title);
        Assert.Equal("C", parsed.Meta.Key);
        Assert.Equal(96, parsed.Meta.Bpm);
        Assert.Equal(14, parsed.Events.Count);
        var sixth = parsed.Events[5];
        Assert.Equal(HandpanEventKind.Note, sixth.Kind);
        Assert.Equal(NoteNames.ToMidi("A4"), sixth.Midi);
        Assert.Equal(1, sixth.Beats);
        Assert.Equal(2, parsed.Events[6].Beats);
    }

    [Fact]
    public void Parse_AppliesTonicAndAccidentals()
    {
        var parsed = ScoreParser.Parse("1=G\n1 2 3 #4 5");

        Assert.Equal("G", parsed.Meta.Key);
        Assert.Equal(NoteNames.ToMidi("G4"), parsed.Events[0].Midi);
        Assert.Equal(NoteNames.ToMidi("A4"), parsed.Events[1].Midi);
        Assert.Equal(NoteNames.ToMidi("B4"), parsed.Events[2].Midi);
        Assert.Equal(NoteNames.ToMidi("C#5"), parsed.Events[3].Midi);
        Assert.Equal(NoteNames.ToMidi("D5"), parsed.Events[4].Midi);
    }

    [Fact]
    public void Parse_HandlesOctavesAndDurations()
    {
        var parsed = ScoreParser.Parse("6, 5' 5'' 3_ 3__ 5:1.5 0 0- 0:2 | 1");

        Assert.Equal(NoteNames.ToMidi("A3"), parsed.Events[0].Midi);
        Assert.Equal(NoteNames.ToMidi("G5"), parsed.Events[1].Midi);
        Assert.Equal(NoteNames.ToMidi("G6"), parsed.Events[2].Midi);
        Assert.Equal(0.5, parsed.Events[3].Beats);
        Assert.Equal(0.25, parsed.Events[4].Beats);
        Assert.Equal(1.5, parsed.Events[5].Beats);
        Assert.Equal(1, parsed.Events[6].Beats);
        Assert.Equal(2, parsed.Events[7].Beats);
        Assert.Equal(2, parsed.Events[8].Beats);
        Assert.Equal(HandpanEventKind.Bar, parsed.Events[9].Kind);
    }

    [Fact]
    public void Parse_ReadsPitchNames()
    {
        var parsed = ScoreParser.Parse("E4 F4.5 G4");

        Assert.Equal(NoteNames.ToMidi("E4"), parsed.Events[0].Midi);
        Assert.Equal(2, parsed.Events[1].Beats);
        Assert.Equal(NoteNames.ToMidi("G4"), parsed.Events[2].Midi);
    }

    [Fact]
    public void Parse_SkipsCommentsAndBlankLines()
    {
        var parsed = ScoreParser.Parse("; 注释\n% 另一种注释\n\n1\n");

        Assert.Single(parsed.Events);
    }

    [Fact]
    public void Parse_ThrowsOnUnknownToken()
    {
        var ex = Assert.Throws<ScoreParseException>(() => ScoreParser.Parse("1 8"));
        Assert.Contains("8", ex.Message);
    }

    [Fact]
    public void Parse_BpmOverideAcceptsDecimals()
    {
        var parsed = ScoreParser.Parse("BPM=90.5\n1");
        Assert.Equal(90.5, parsed.Meta.Bpm);
    }
}
