namespace RocoPilot.Detection;

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

    public static string ToDisplay(string className) =>
        s_toChinese.TryGetValue(className, out var zh) ? zh : className;

    public static string ToKey(string input) =>
        s_toKey.TryGetValue(input, out var key) ? key : input;
}
