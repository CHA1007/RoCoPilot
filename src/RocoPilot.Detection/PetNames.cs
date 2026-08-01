namespace RocoPilot.Detection;

/// <summary>精灵类名中英文映射。模型输出英文 key，UI 显示中文名。</summary>
public static class PetNames
{
    private static readonly Dictionary<string, string> s_toChinese = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yaxiaxuexiong"] = "月牙雪熊",
        ["emolang"] = "恶魔狼",
        ["xingyunlvzhe"] = "星云旅者",
    };

    private static readonly Dictionary<string, string> s_toKey;

    static PetNames()
    {
        s_toKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, zh) in s_toChinese)
        {
            s_toKey[zh] = key;
        }
    }

    /// <summary>英文 key → 中文名；无映射则原样返回。</summary>
    public static string ToDisplay(string className) =>
        s_toChinese.TryGetValue(className, out var zh) ? zh : className;

    /// <summary>中文名 → 英文 key；已是 key 或非中文则原样返回。</summary>
    public static string ToKey(string input) =>
        s_toKey.TryGetValue(input, out var key) ? key : input;
}
