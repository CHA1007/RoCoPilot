using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

public class ChartBuilderTests
{
    private static readonly KeyMap DefaultMap = new(KeyMap.DefaultEntries);

    private static MidiNote Note(double start, double end, int pitch) => new(0, pitch, start, end);

    private static MidiMeta Meta(
        double bpm = 120,
        MusicKey? tonic = null,
        TimeSignature? timeSignature = null,
        int tempoChanges = 0) =>
        new(bpm, tonic, timeSignature ?? new TimeSignature(4, 4), tempoChanges);

    private static string Sequence(HandpanChart chart) =>
        string.Join(" / ", chart.Lines.Where(line => line.StartsWith("  |")));

    [Fact]
    public void An_empty_line_yields_only_the_header()
    {
        var chart = ChartBuilder.Build([], Meta(tonic: new MusicKey("C", false)), DefaultMap, "空");

        Assert.Equal(
            ["《空》 手碟按键谱", "调号 1=C  BPM=120  拍号 4/4  1拍=0.500秒  总时长≈0秒"],
            chart.Lines);
        Assert.Equal(0, chart.TotalBeats);
        Assert.Empty(chart.Missing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void An_unknown_title_reads_as_untitled(string? title)
    {
        var chart = ChartBuilder.Build([], Meta(), DefaultMap, title);

        Assert.StartsWith("《未命名》", chart.Lines[0]);
    }

    [Fact]
    public void The_header_reports_key_tempo_signature_and_length()
    {
        var chart = ChartBuilder.Build([Note(0, 3, 72)], Meta(tonic: new MusicKey("F#", false)), DefaultMap, "晴天");

        Assert.Equal("《晴天》 手碟按键谱", chart.Lines[0]);
        Assert.Equal("调号 1=F#  BPM=120  拍号 4/4  1拍=0.500秒  总时长≈1.5秒", chart.Lines[1]);
    }

    [Fact]
    public void A_flattened_tempo_map_warns_after_the_header()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(tempoChanges: 2), DefaultMap);

        Assert.Equal("警告：检测到 2 处速度变化，已统一为 120 BPM", chart.Lines[2]);
    }

    [Fact]
    public void A_single_tempo_never_warns()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(), DefaultMap);

        Assert.Equal("", chart.Lines[2]);
    }

    [Fact]
    public void An_unknown_key_is_left_out_of_the_header()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(bpm: 150, tonic: null), DefaultMap);

        Assert.Equal("BPM=150  拍号 4/4  1拍=0.400秒  总时长≈0.4秒", chart.Lines[1]);
    }

    [Fact]
    public void The_legend_lists_used_notes_lowest_first()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 76), Note(1, 2, 72)], Meta(), DefaultMap);

        Assert.Equal(["    C5 : T", "    E5 : U"], chart.Lines.Where(line => line.Contains(" : ")));
    }

    [Fact]
    public void The_legend_flags_notes_the_handpan_lacks()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 73)], Meta(), DefaultMap);

        Assert.Contains("   C#5 : ??  ← 手碟上没有这个音", chart.Lines);
    }

    [Fact]
    public void The_sequence_shows_keys_with_their_beats()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(1, 1.5, 74), Note(2, 3, 76)], Meta(), DefaultMap);

        Assert.Equal("  |  1| T(1) Y(0.5) 0(0.5) U(1)", Sequence(chart));
    }

    [Fact]
    public void A_one_beat_rest_shows_a_bare_zero()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(2, 3, 74)], Meta(), DefaultMap);

        Assert.Equal("  |  1| T(1) 0 Y(1)", Sequence(chart));
    }

    [Fact]
    public void A_leading_rest_opens_the_sequence()
    {
        var chart = ChartBuilder.Build([Note(1, 2, 72)], Meta(), DefaultMap);

        Assert.Equal("  |  1| 0 T(1)", Sequence(chart));
    }

    [Fact]
    public void A_micro_gap_is_absorbed_into_the_previous_note()
    {
        var chart = ChartBuilder.Build([Note(0, 0.98, 72), Note(1, 2, 74)], Meta(), DefaultMap);

        Assert.Equal("  |  1| T(1) Y(1)", Sequence(chart));
    }

    [Fact]
    public void An_overlapping_note_is_clipped_to_the_next_onset()
    {
        var chart = ChartBuilder.Build([Note(0, 3, 72), Note(1, 2, 76)], Meta(), DefaultMap);

        Assert.Equal("  |  1| T(1) U(1)", Sequence(chart));
    }

    [Fact]
    public void Notes_sharing_an_onset_render_as_one_chord()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(0, 1, 76)], Meta(), DefaultMap);

        Assert.Equal("  |  1| U+T(1)", Sequence(chart));
    }

    [Fact]
    public void A_chord_marks_only_the_notes_the_handpan_lacks()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(0, 1, 73)], Meta(), DefaultMap);

        Assert.Equal("  |  1| [C#5缺失]+T(1)", Sequence(chart));
    }

    [Fact]
    public void Bars_are_numbered_by_their_position()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(4, 5, 74)], Meta(), DefaultMap);

        Assert.Equal("  |  1| T(1) 0(3) /   |  2| Y(1)", Sequence(chart));
    }

    [Fact]
    public void Bars_follow_the_time_signature()
    {
        var chart = ChartBuilder.Build(
            [Note(0, 1, 72), Note(3, 4, 74)],
            Meta(timeSignature: new TimeSignature(6, 8)),
            DefaultMap);

        Assert.Equal("  |  1| T(1) 0(2) /   |  2| Y(1)", Sequence(chart));
    }

    [Fact]
    public void A_missing_note_is_counted_with_its_position()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72), Note(2, 3, 73)], Meta(), DefaultMap);

        Assert.Equal(1, chart.MissingCount);
        Assert.Equal(new MissingNote(73, 2, 1), Assert.Single(chart.Missing));
        Assert.Equal("C#5", Assert.Single(chart.Missing).NoteName);
        Assert.Contains("警告：1 个音不在手碟音域内：C#5(2拍)", chart.Lines);
    }

    [Fact]
    public void The_warning_preview_stops_at_twelve_notes()
    {
        var line = Enumerable.Range(0, 13).Select(index => Note(index, index + 1, 30)).ToList();

        var chart = ChartBuilder.Build(line, Meta(), DefaultMap);

        var warning = Assert.Single(chart.Lines, line => line.StartsWith("警告"));
        Assert.Equal(13, chart.MissingCount);
        Assert.EndsWith("…", warning);
        Assert.Equal(12, warning.Split('：')[2].Split('、').Length);
    }

    [Fact]
    public void A_chart_without_missing_notes_carries_no_warning()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(), DefaultMap);

        Assert.DoesNotContain(chart.Lines, line => line.StartsWith("警告"));
    }

    [Fact]
    public void The_chart_text_joins_its_lines()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(), DefaultMap, "测试");

        Assert.Equal(string.Join(Environment.NewLine, chart.Lines), chart.Text);
        Assert.Equal(0.5, chart.SecondsPerBeat);
        Assert.Equal(1, chart.TotalBeats);
        Assert.Equal(0.5, chart.TotalSeconds);
    }

    [Fact]
    public void The_chart_sections_are_in_reading_order()
    {
        var chart = ChartBuilder.Build([Note(0, 1, 72)], Meta(tonic: new MusicKey("C", false)), DefaultMap, "测试");

        Assert.Equal(
            ["《测试》 手碟按键谱", "调号 1=C  BPM=120  拍号 4/4  1拍=0.500秒  总时长≈0.5秒",
                "", "【音名 → 按键】", "    C5 : T",
                "", "【演奏序列】(括号内为拍数，0 = 休止)", "  |  1| T(1)"],
            chart.Lines);
    }
}
