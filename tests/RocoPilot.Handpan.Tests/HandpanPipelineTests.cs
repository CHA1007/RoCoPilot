using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class HandpanPipelineTests
{
    private static readonly KeyMap KeyMap = new(KeyMap.DefaultEntries);

    private static HandpanOptions Options(
        double bpm = 0, int transpose = 0, bool autoFit = false,
        bool fold = false, bool snap = false, double gap = 0) =>
        new()
        {
            BpmOverride = bpm,
            Transpose = transpose,
            AutoFit = autoFit,
            Fold = fold,
            Snap = snap,
            GapBeats = gap,
        };

    [Fact]
    public void Run_ReportsBpmOverride()
    {
        var score = ScoreParser.Parse("1=C\nbpm=90\n1=E\n3");
        var result = HandpanPipeline.Run(score, KeyMap, Options(bpm: 180));

        Assert.Equal(180, result.Meta.Bpm);
        Assert.Contains(result.ReportLines, l => l.Contains("180"));
    }

    [Fact]
    public void Run_AppliesTranspose()
    {
        var score = ScoreParser.Parse("1=E\n3 4");
        var result = HandpanPipeline.Run(score, KeyMap, Options(transpose: 1));

        Assert.Equal(NoteNames.ToMidi("A4"), result.Events[0].Midi);
        Assert.Contains(result.ReportLines, l => l.Contains("移调"));
    }

    [Fact]
    public void Run_AutoFitFindsPlayableKey()
    {
        var score = ScoreParser.Parse("1=C\n1 2 3 4 5 6 7 1'");
        var result = HandpanPipeline.Run(score, KeyMap, Options(autoFit: true));

        Assert.Equal(0, result.MissingCount);
        Assert.Contains(result.ReportLines, l => l.Contains("自动移调"));
    }

    [Fact]
    public void Run_FoldAndSnapRepairMissingNotes()
    {
        var score = ScoreParser.Parse("1=C\n1 2 3");
        var result = HandpanPipeline.Run(score, KeyMap, Options(fold: true, snap: true));

        Assert.True(result.MissingCount < 2);
    }

    [Fact]
    public void Run_GapExtendsNotes()
    {
        var score = ScoreParser.Parse("1=E\n3 3");
        var result = HandpanPipeline.Run(score, KeyMap, Options(gap: 0.25));

        Assert.Equal(1.25, result.Events[0].Beats);
    }

    [Fact]
    public void Run_ProducesChartAndPlan()
    {
        var score = ScoreParser.Parse("1=C\n3 4 5-");
        var result = HandpanPipeline.Run(score, KeyMap, Options());

        Assert.Contains("【演奏序列】", result.Chart.Text);
        Assert.Equal(3, result.Plan.Notes.Count);
        Assert.Equal(0, result.MissingCount);
    }
}
