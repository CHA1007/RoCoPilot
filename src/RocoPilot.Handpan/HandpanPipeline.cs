namespace RocoPilot.Handpan;

public sealed record HandpanOptions
{
    public double BpmOverride { get; init; }

    public int Transpose { get; init; }

    public bool AutoFit { get; init; }

    public bool Fold { get; init; }

    public bool Snap { get; init; }

    public double GapBeats { get; init; }
}

public sealed record HandpanPipelineResult(
    IReadOnlyList<HandpanEvent> Events,
    ScoreMeta Meta,
    ChartResult Chart,
    PlaybackPlan Plan,
    IReadOnlyList<string> ReportLines)
{
    public int MissingCount => Chart.MissingCount;
}

public static class HandpanPipeline
{
    public static HandpanPipelineResult Run(ParsedScore score, KeyMap keyMap, HandpanOptions options)
    {
        var report = new List<string>();
        var meta = score.Meta;
        if (options.BpmOverride > 0)
        {
            meta = meta with { Bpm = options.BpmOverride };
            report.Add($"BPM 覆盖为 {options.BpmOverride:0.##}");
        }

        var events = score.Events;
        int shift = 0;
        if (options.Transpose != 0)
        {
            shift = options.Transpose;
            events = HandpanFitting.Transpose(events, shift);
            report.Add($"已移调 {shift:+0;-0} 半音");
        }
        else if (options.AutoFit)
        {
            (shift, var coverage) = HandpanFitting.BestTransposition(events, keyMap);
            events = HandpanFitting.Transpose(events, shift);
            var (folded, foldedCount) = HandpanFitting.Fold(events, keyMap);
            events = folded;
            var (snapped, snappedCount) = HandpanFitting.Snap(events, keyMap, tonic: meta.Key);
            events = snapped;
            var (snappedWide, snappedWideCount) = HandpanFitting.Snap(events, keyMap, maxSemitones: 4, tonic: meta.Key);
            events = snappedWide;
            report.Add($"自动移调 {shift:+0;-0} 半音（精确覆盖率 {coverage:P0}，折回 {foldedCount} 个、吸附 {snappedCount} 个、宽吸 ±4 半音兑底 {snappedWideCount} 个）");
        }

        if (options.Fold)
        {
            var (folded, foldedCount) = HandpanFitting.Fold(events, keyMap);
            events = folded;
            report.Add(foldedCount > 0
                ? $"已把 {foldedCount} 个音移八度折回手碟音域"
                : "没有需要折回的音符");
        }

        if (options.Snap)
        {
            var (snapped, snappedCount) = HandpanFitting.Snap(events, keyMap, tonic: meta.Key);
            events = snapped;
            report.Add(snappedCount > 0
                ? $"已把 {snappedCount} 个音吸附到最近的可用音"
                : "没有需要吸附的音符");
        }

        if (options.GapBeats > 0)
        {
            events = HandpanFitting.Gap(events, options.GapBeats);
            report.Add($"每个音后垫 {options.GapBeats:0.##} 拍空隙");
        }

        events = HandpanFitting.DedupeExtras(events).ToList();
        var chart = ChartBuilder.Build(events, meta, keyMap);
        var plan = PlaybackPlanner.Plan(events, meta.Bpm, keyMap);
        return new HandpanPipelineResult(events, meta, chart, plan, report);
    }
}
