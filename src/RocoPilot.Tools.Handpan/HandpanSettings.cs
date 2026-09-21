using System.Text.Json.Serialization;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan;

public sealed record ScoreProfile(
    int Transpose = 0,
    int MinIntervalMs = 0,
    double RestrikeIntervalBeats = 0,
    bool Accompany = false,
    double AccompanyEveryBeats = 2,
    bool SpeedByPercent = false,
    double SpeedPercent = 100,
    double BpmOverride = 0);

public sealed class HandpanSettings
{
    private static readonly HandpanSettings Baseline = new();

    [JsonIgnore]
    public HandpanMode Mode { get; set; } = HandpanMode.Play;

    public string ScoreName { get; set; } = string.Empty;

    public Dictionary<string, ScoreProfile> ScoreProfiles { get; set; } = [];

    public List<KeyMapEntry> KeyMapEntries { get; set; } = [.. KeyMap.DefaultEntries];

    public int Transpose { get; set; }

    public int MinIntervalMs { get; set; }

    public bool SpeedByPercent { get; set; }

    public double SpeedPercent { get; set; } = 100;

    public double BpmOverride { get; set; }

    public bool Loop { get; set; }

    public int CountdownSeconds { get; set; } = 3;

    public int HoldMs { get; set; } = 50;

    public int ChordHoldMs { get; set; } = 60;

    public int ChordStaggerMs { get; set; } = 12;

    public double RestrikeIntervalBeats { get; set; }

    public bool Accompany { get; set; }

    public double AccompanyEveryBeats { get; set; } = 2;

    public KeyMap ToKeyMap() => new(KeyMapEntries);

    public PlaybackTiming ToTiming() => new(
        HoldMs / 1000.0,
        ChordHoldMs / 1000.0,
        ChordStaggerMs / 1000.0,
        RestrikeIntervalBeats);

    public ArrangementOptions ToArrangement() =>
        new(
            Transpose,
            MinIntervalMs / 1000.0,
            SpeedByPercent ? 0 : BpmOverride,
            SpeedByPercent ? SpeedPercent : 100,
            Accompany,
            AccompanyEveryBeats,
            ToTiming());

    public double SpeedPercentFor(double scoreBpm) =>
        BpmOverride > 0 && scoreBpm > 0 ? Math.Round(BpmOverride * 100.0 / scoreBpm) : 100;

    public double BpmOverrideFor(double scoreBpm) =>
        Math.Abs(SpeedPercent - 100) > 1e-9 && scoreBpm > 0 ? Math.Round(scoreBpm * SpeedPercent / 100.0) : 0;

    public ScoreProfile ProfileOf(string? scoreName) =>
        !string.IsNullOrWhiteSpace(scoreName)
            && ScoreProfiles.TryGetValue(scoreName, out var profile)
            ? profile
            : new ScoreProfile();

    public void SaveProfile(string? scoreName)
    {
        if (string.IsNullOrWhiteSpace(scoreName))
        {
            return;
        }

        ScoreProfiles[scoreName] = new ScoreProfile(
            Transpose,
            MinIntervalMs,
            RestrikeIntervalBeats,
            Accompany,
            AccompanyEveryBeats,
            SpeedByPercent,
            SpeedPercent,
            BpmOverride);
    }

    public void SanitizeInPlace()
    {
        ScoreName = (ScoreName ?? string.Empty).Trim();
        KeyMapEntries = SanitizeKeyMap(KeyMapEntries);
        Transpose = (int)Clamp(Transpose, -11, 11);
        MinIntervalMs = (int)Clamp(MinIntervalMs, 0, 300);
        SpeedPercent = FiniteOrBaseline(SpeedPercent, Baseline.SpeedPercent, 50, 200);
        BpmOverride = FiniteOrBaseline(BpmOverride, Baseline.BpmOverride, 0, 240);
        CountdownSeconds = (int)Clamp(CountdownSeconds, 0, 10);
        HoldMs = (int)Clamp(HoldMs, 20, 500);
        ChordHoldMs = (int)Clamp(ChordHoldMs, 20, 500);
        ChordStaggerMs = (int)Clamp(ChordStaggerMs, 0, 200);
        RestrikeIntervalBeats = FiniteOrBaseline(RestrikeIntervalBeats, Baseline.RestrikeIntervalBeats, 0, 8);
        AccompanyEveryBeats = FiniteOrBaseline(AccompanyEveryBeats, Baseline.AccompanyEveryBeats, 0.5, 8);
        ScoreProfiles = SanitizeProfiles(ScoreProfiles);
    }

    private static Dictionary<string, ScoreProfile> SanitizeProfiles(Dictionary<string, ScoreProfile>? profiles) =>
        profiles?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value is not null)
            .GroupBy(entry => entry.Key.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => SanitizeProfile(group.First().Value),
                StringComparer.Ordinal)
        ?? [];

    private static ScoreProfile SanitizeProfile(ScoreProfile profile) => new(
        (int)Clamp(profile.Transpose, -11, 11),
        (int)Clamp(profile.MinIntervalMs, 0, 300),
        FiniteOrBaseline(profile.RestrikeIntervalBeats, 0, 0, 8),
        profile.Accompany,
        FiniteOrBaseline(profile.AccompanyEveryBeats, 2, 0.5, 8),
        profile.SpeedByPercent,
        FiniteOrBaseline(profile.SpeedPercent, 100, 50, 200),
        FiniteOrBaseline(profile.BpmOverride, 0, 0, 240));

    private static List<KeyMapEntry> SanitizeKeyMap(List<KeyMapEntry>? entries) =>
        entries?
            .Where(entry => entry is not null)
            .GroupBy(entry => entry.Note.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList() is { Count: > 0 } sanitized
            ? sanitized
            : [.. KeyMap.DefaultEntries];

    private static double FiniteOrBaseline(double value, double baseline, double min, double max) =>
        double.IsFinite(value) ? Clamp(value, min, max) : baseline;

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
}
