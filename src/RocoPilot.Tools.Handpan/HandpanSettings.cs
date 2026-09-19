using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan;

public sealed class HandpanSettings
{
    public const string DefaultScoreText = """
        title=小星星
        1=C
        bpm=96
        1 1 5 5 6 6 5-
        4 4 3 3 2 2 1-
        """;

    public string ScoreText { get; set; } = DefaultScoreText;

    public List<KeyMapEntry> KeyMapEntries { get; set; } = [.. KeyMap.DefaultEntries];

    public double BpmOverride { get; set; }

    public int Transpose { get; set; }

    public bool AutoFit { get; set; } = true;

    public bool Fold { get; set; } = true;

    public bool Snap { get; set; } = true;

    public double GapBeats { get; set; }

    public bool Loop { get; set; }

    public int HoldMs { get; set; } = 50;

    public int ChordStaggerMs { get; set; } = 12;

    public int CountdownSeconds { get; set; } = 3;

    public KeyMap ToKeyMap() => new(KeyMapEntries);

    public void SanitizeInPlace()
    {
        ScoreText ??= string.Empty;
        KeyMapEntries = KeyMapEntries?
            .Where(e => e is not null)
            .GroupBy(e => e.Note?.Trim().ToUpperInvariant() ?? string.Empty)
            .Select(g => g.First())
            .ToList() ?? [.. KeyMap.DefaultEntries];
        BpmOverride = double.IsFinite(BpmOverride) ? Math.Clamp(BpmOverride, 0, 600) : 0;
        Transpose = Math.Clamp(Transpose, -11, 11);
        GapBeats = double.IsFinite(GapBeats) ? Math.Clamp(GapBeats, 0, 4) : 0;
        HoldMs = (int)Math.Clamp(HoldMs, 20, 500);
        ChordStaggerMs = (int)Math.Clamp(ChordStaggerMs, 0, 200);
        CountdownSeconds = (int)Math.Clamp(CountdownSeconds, 0, 10);
    }
}
