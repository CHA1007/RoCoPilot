using System.Globalization;
using System.Text;

namespace RocoPilot.Handpan;

public static class ChartBuilder
{
    private const long RestThresholdUnits = 4;
    private const int MissingPreviewLimit = 12;
    private const string UntitledScore = "未命名";
    private const string UnmappedKey = "??";
    private const string UnmappedKeyMark = "  ← 手碟上没有这个音";

    public static HandpanChart Build(
        IReadOnlyList<MidiNote> notes,
        MidiMeta meta,
        KeyMap keyMap,
        string? title = null)
    {
        var secondsPerBeat = 60 / meta.Bpm;
        var totalBeats = MelodyLine.TotalBeats(notes);
        var tokens = Tokens(MelodyLine.OnsetGroups(notes));
        var missing = Missing(tokens, keyMap);

        var lines = new List<string>
        {
            $"《{(string.IsNullOrWhiteSpace(title) ? UntitledScore : title)}》 手碟按键谱",
            Header(meta, secondsPerBeat, totalBeats),
        };
        lines.AddRange(KeyLegend(tokens, keyMap));
        lines.AddRange(Bars(tokens, meta.TimeSignature.BeatsPerBar, keyMap));
        lines.AddRange(MissingWarning(missing));
        return new HandpanChart(lines, missing, totalBeats, secondsPerBeat);
    }

    private sealed record Token(long StartUnits, long BeatUnits, IReadOnlyList<MidiNote> Notes)
    {
        public bool IsRest => Notes.Count == 0;

        public double Beats => BeatGrid.ToBeats(BeatUnits);
    }

    private static IReadOnlyList<Token> Tokens(IReadOnlyList<IReadOnlyList<MidiNote>> groups)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var start = BeatGrid.ToUnits(group[0].StartBeat);
            var end = group.Max(note => BeatGrid.ToUnits(note.EndBeat));
            var next = index + 1 < groups.Count ? BeatGrid.ToUnits(groups[index + 1][0].StartBeat) : end;
            if (tokens.Count == 0 && start >= RestThresholdUnits)
            {
                tokens.Add(new Token(0, start, []));
            }

            var gapUnits = next - end;
            var beatUnits = gapUnits >= RestThresholdUnits
                ? Math.Max(1, end - start)
                : Math.Max(1, next - start);
            tokens.Add(new Token(start, beatUnits, group));
            if (gapUnits >= RestThresholdUnits)
            {
                tokens.Add(new Token(start + beatUnits, next - start - beatUnits, []));
            }
        }

        return tokens;
    }

    private static IReadOnlyList<MissingNote> Missing(IReadOnlyList<Token> tokens, KeyMap keyMap) =>
        [.. tokens
            .Where(token => !token.IsRest)
            .SelectMany(token => token.Notes
                .Where(note => !keyMap.Contains(note.Pitch))
                .Select(note => new MissingNote(note.Pitch, note.StartBeat, token.Beats)))];

    private static IEnumerable<string> KeyLegend(IReadOnlyList<Token> tokens, KeyMap keyMap)
    {
        var pitches = tokens
            .Where(token => !token.IsRest)
            .SelectMany(token => token.Notes)
            .Select(note => note.Pitch)
            .Distinct()
            .OrderBy(pitch => pitch)
            .ToList();
        if (pitches.Count == 0)
        {
            return [];
        }

        var lines = new List<string> { string.Empty, "【音名 → 按键】" };
        foreach (var pitch in pitches)
        {
            lines.Add(keyMap.TryGet(pitch, out var key)
                ? $"  {NoteNames.Of(pitch),4} : {key}"
                : $"  {NoteNames.Of(pitch),4} : {UnmappedKey}{UnmappedKeyMark}");
        }

        return lines;
    }

    private static IEnumerable<string> Bars(IReadOnlyList<Token> tokens, double beatsPerBar, KeyMap keyMap)
    {
        if (tokens.Count == 0)
        {
            return [];
        }

        var barUnits = Math.Max(1, (long)Math.Round(beatsPerBar * BeatGrid.UnitsPerBeat));
        var lines = new List<string> { string.Empty, "【演奏序列】(括号内为拍数，0 = 休止)" };
        foreach (var bar in tokens.GroupBy(token => token.StartUnits / barUnits))
        {
            lines.Add($"  |{bar.Key + 1,3}| " + string.Join(" ", bar.Select(token => Render(token, keyMap))));
        }

        return lines;
    }

    private static IEnumerable<string> MissingWarning(IReadOnlyList<MissingNote> missing)
    {
        if (missing.Count == 0)
        {
            return [];
        }

        var preview = string.Join(
            "、",
            missing.Take(MissingPreviewLimit)
                .Select(note => $"{note.NoteName}({Format(note.StartBeat)}拍)"));
        return
        [
            string.Empty,
            $"警告：{missing.Count} 个音不在手碟音域内：{preview}"
            + (missing.Count > MissingPreviewLimit ? "…" : string.Empty),
        ];
    }

    private static string Render(Token token, KeyMap keyMap) => token.IsRest
        ? token.BeatUnits == BeatGrid.UnitsPerBeat ? "0" : $"0({Format(token.Beats)})"
        : $"{string.Join("+", token.Notes.Select(note => Key(note, keyMap)))}({Format(token.Beats)})";

    private static string Key(MidiNote note, KeyMap keyMap) => keyMap.TryGet(note.Pitch, out var key)
        ? key
        : $"[{NoteNames.Of(note.Pitch)}缺失]";

    private static string Header(MidiMeta meta, double secondsPerBeat, double totalBeats)
    {
        var header = new StringBuilder();
        if (meta.Key is { } key)
        {
            header.Append(key.RelativeMajor is { } major
                ? $"调号 1={major}（{key.Tonic} 小调）  "
                : $"调号 1={key.Tonic}  ");
        }

        return header
            .Append($"BPM={Format(meta.Bpm, "0")}  ")
            .Append($"拍号 {meta.TimeSignature.Numerator}/{meta.TimeSignature.Denominator}  ")
            .Append($"1拍={Format(secondsPerBeat, "0.000")}秒  ")
            .Append($"总时长≈{Format(totalBeats * secondsPerBeat, "0.#")}秒")
            .ToString();
    }

    private static string Format(double value, string format = "0.###") =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
