namespace RocoPilot.Routing;

public sealed record AnchorEntry
{
    public AnchorEntry(string name, double x, double y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (x is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "锚点归一化 X 必须在 [0,1] 区间。");
        if (y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(y), "锚点归一化 Y 必须在 [0,1] 区间。");

        Name = name.Trim();
        X = x;
        Y = y;
    }

    public string Name { get; }

    public double X { get; }

    public double Y { get; }
}
