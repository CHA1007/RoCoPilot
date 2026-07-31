using System.Text.RegularExpressions;

namespace RocoPilot.Detection.Inference;

internal static partial class YoloClassNames
{
    public static IReadOnlyList<string> Parse(string namesRaw)
    {
        if (string.IsNullOrWhiteSpace(namesRaw))
            throw new DetectionException("模型元数据 names 为空——须为 ultralytics 导出（票 04 export_onnx.py）");

        var matches = EntryRegex().Matches(namesRaw);
        if (matches.Count == 0)
            throw new DetectionException($"模型元数据 names 无法解析（期望形如 {{0: 'x', 1: 'y'}}），实得：{namesRaw}");

        var byIndex = new SortedDictionary<int, string>();
        foreach (Match match in matches)
        {
            var index = int.Parse(match.Groups[1].ValueSpan);
            var name = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(name))
                throw new DetectionException($"模型元数据 names 第 {index} 类名为空：{namesRaw}");
            if (!byIndex.TryAdd(index, name))
                throw new DetectionException($"模型元数据 names 下标 {index} 重复：{namesRaw}");
        }

        var names = new string[byIndex.Count];
        var expected = 0;
        foreach (var (index, name) in byIndex)
        {
            if (index != expected)
                throw new DetectionException($"模型元数据 names 下标须从 0 连续，缺 {expected}：{namesRaw}");
            names[expected++] = name;
        }

        return names;
    }

    [GeneratedRegex(@"(\d+)\s*:\s*'([^']*)'")]
    private static partial Regex EntryRegex();
}
