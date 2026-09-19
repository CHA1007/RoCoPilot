using System.Globalization;
using System.Text;

namespace RocoPilot.Handpan;

public sealed record ChartResult(string Text, int MissingCount);

public static class ChartBuilder
{
    public static ChartResult Build(IReadOnlyList<HandpanEvent> events, ScoreMeta meta, KeyMap keyMap)
    {
        var secondsPerBeat = 60.0 / meta.Bpm;
        var lines = new List<string>();
        var missing = new List<(string Token, string NoteName)>();
        var used = new SortedDictionary<int, string>();

        foreach (var e in events)
        {
            if (e.Kind != HandpanEventKind.Note)
            {
                continue;
            }

            used[e.Midi] = keyMap.TryGet(e.Midi, out var key) ? key : "??";
        }

        var title = string.IsNullOrEmpty(meta.Title) ? "(未命名)" : meta.Title;
        lines.Add($"《{title}》 手碟按键谱");
        lines.Add($"调号 1={meta.Key}  BPM={Fmt(meta.Bpm)}  1拍={Fmt(secondsPerBeat)}秒  总时长≈{Fmt(TotalBeats(events) * secondsPerBeat)}秒");
        lines.Add(string.Empty);
        lines.Add("【音名 -> 按键 对照】");
        foreach (var (midi, key) in used)
        {
            var degree = NoteNames.DegreeZh(midi);
            var degreeText = degree is null ? string.Empty : $"   唱名 {degree}";
            var mark = key == "??" ? "  ← 游戏里没有这个音！" : string.Empty;
            lines.Add($"  {NoteNames.Of(midi),4} : {key}{degreeText}{mark}");
        }

        lines.Add(string.Empty);
        lines.Add("【演奏序列】(括号内为拍数，0 = 休止)");
        var bars = new List<List<string>>();
        var currentBar = new List<string>();
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case HandpanEventKind.Bar:
                    if (currentBar.Count > 0)
                    {
                        bars.Add(currentBar);
                        currentBar = [];
                    }

                    break;
                case HandpanEventKind.Note:
                    if (keyMap.TryGet(e.Midi, out var key))
                    {
                        var token = key;
                        if (e.Extras is { Count: > 0 })
                        {
                            foreach (var extra in e.Extras)
                            {
                                if (keyMap.TryGet(extra, out var extraKey))
                                {
                                    token += "+" + extraKey;
                                }
                            }
                        }

                        currentBar.Add($"{token}({Fmt(e.Beats)})");
                    }
                    else
                    {
                        missing.Add((e.Token, e.NoteName));
                        currentBar.Add($"[{e.NoteName}缺失]({Fmt(e.Beats)})");
                    }

                    break;
                default:
                    currentBar.Add(Math.Abs(e.Beats - 1) < 1e-9 ? "0" : $"0({Fmt(e.Beats)})");
                    break;
            }
        }

        if (currentBar.Count > 0)
        {
            bars.Add(currentBar);
        }

        for (var i = 0; i < bars.Count; i++)
        {
            lines.Add($"  |{i + 1,3}| " + string.Join(" ", bars[i]));
        }

        if (missing.Count > 0)
        {
            lines.Add(string.Empty);
            var preview = missing.Take(12).Select(m => $"{m.Token}({m.NoteName})");
            lines.Add($"警告：有 {missing.Count} 个音在手碟音域之外: " + string.Join("、", preview) +
                      (missing.Count > 12 ? "..." : string.Empty));
        }

        return new ChartResult(string.Join("\n", lines), missing.Count);
    }

    public static double TotalBeats(IReadOnlyList<HandpanEvent> events) =>
        events.Sum(e => e.Beats);

    private static string Fmt(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture);
}
