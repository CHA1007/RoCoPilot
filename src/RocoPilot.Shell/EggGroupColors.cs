using System.Windows.Media;

namespace RocoPilot.Shell;

internal static class EggGroupColors
{
    private static readonly IReadOnlyDictionary<string, string> Hex = new Dictionary<string, string>
    {
        ["未发现"] = "#777d84",
        ["动物组"] = "#a56658",
        ["拟人组"] = "#86c8a7",
        ["巨灵组"] = "#7ea56b",
        ["魔力组"] = "#4f83ab",
        ["天空组"] = "#efd779",
        ["两栖组"] = "#d86361",
        ["植物组"] = "#d98dcf",
        ["大地组"] = "#91784d",
        ["妖精组"] = "#685b8c",
        ["昆虫组"] = "#59a0d8",
        ["软体组"] = "#7c8996",
        ["机械组"] = "#73848b",
        ["海洋组"] = "#438cac",
        ["飞龙组"] = "#826bb3",
    };

    public static SolidColorBrush Of(string group)
    {
        var hex = Hex.TryGetValue(group, out var value) ? value : "#777d84";
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
