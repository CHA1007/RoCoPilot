namespace RocoPilot.Handpan;

public enum HandpanEventKind
{
    Note,
    Rest,
    Bar,
}

public sealed record HandpanEvent(
    HandpanEventKind Kind,
    int Midi,
    double Beats,
    string Token = "",
    string NoteName = "",
    IReadOnlyList<int>? Extras = null)
{
    public static HandpanEvent Bar() => new(HandpanEventKind.Bar, 0, 0);

    public static HandpanEvent Rest(double beats) => new(HandpanEventKind.Rest, 0, beats);

    public static HandpanEvent Note(int midi, double beats, string token, string noteName,
        IReadOnlyList<int>? extras = null) =>
        new(HandpanEventKind.Note, midi, beats, token, noteName, extras);
}
