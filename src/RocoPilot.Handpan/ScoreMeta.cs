namespace RocoPilot.Handpan;

public sealed record ScoreMeta(string Key, double Bpm, string Title)
{
    public static ScoreMeta Default() => new("C", 120, string.Empty);
}
