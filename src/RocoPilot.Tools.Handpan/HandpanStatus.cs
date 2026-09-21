using System.Globalization;
using RocoPilot.Core;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan;

public enum HandpanStatusLevel
{
    Normal,

    Caution,

    Critical,
}

public sealed record HandpanStatusView(
    string Headline,
    string? Detail = null,
    HandpanStatusLevel Level = HandpanStatusLevel.Normal);

public static class HandpanStatus
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static HandpanStatusView Of(HandpanArranged arranged) => new("演奏中", Summary(arranged));

    public static HandpanStatusView Of(HandpanCountdown countdown) => new($"{countdown.SecondsLeft} 秒后开始");

    public static HandpanStatusView Of(HandpanPaused paused) =>
        new("已暂停", paused.Source == PauseSource.FocusLost ? "游戏失焦" : "手动暂停", HandpanStatusLevel.Caution);

    public static HandpanStatusView Of(HandpanResumed resumed) => new("演奏中");

    public static HandpanStatusView Of(HandpanRoundCompleted round) => new("演奏中");

    public static HandpanStatusView Of(HandpanCompleted completed) => new("演奏完成");

    public static HandpanStatusView Of(HandpanProbeKey probeKey) => new(probeKey.Key, $"应发 {probeKey.Note}");

    public static HandpanStatusView Of(HandpanProbeCompleted completed) => new("自检完成", $"{completed.KeyCount} 个键");

    public static HandpanStatusView Of(HandpanFaulted faulted) =>
        new("故障", CauseOf(faulted), HandpanStatusLevel.Critical);

    public static HandpanStatusView ArmingStep(ToolEvent toolEvent)
    {
        var step = ValueOf(toolEvent, "step");
        return step is null
            ? new HandpanStatusView("准备中")
            : new HandpanStatusView($"正在{step}", ValueOf(toolEvent, "hint"));
    }

    public static HandpanStatusView ArmingFailed(ToolEvent toolEvent)
    {
        var error = ValueOf(toolEvent, "error");
        var remedy = ValueOf(toolEvent, "remedy");
        return new HandpanStatusView(
            "启动失败",
            error is null ? null : remedy is null ? error : $"{error}。{remedy}",
            HandpanStatusLevel.Critical);
    }

    public static HandpanStatusView Imported() => new("已导入曲谱");

    private static string Summary(HandpanArranged arranged)
    {
        var detail = string.Create(Culture, $"{arranged.NoteCount} 音");
        if (arranged.MissingCount > 0)
        {
            detail += string.Create(Culture, $" · 缺音 {arranged.MissingCount} 个");
        }

        return detail;
    }

    private static string? CauseOf(HandpanFaulted faulted) =>
        faulted.Remedy is { } remedy ? $"{faulted.Error}。{remedy}" : faulted.Error;

    private static string? ValueOf(ToolEvent toolEvent, string key) =>
        toolEvent.Data is not null && toolEvent.Data.TryGetValue(key, out var value) ? value?.ToString() : null;
}
