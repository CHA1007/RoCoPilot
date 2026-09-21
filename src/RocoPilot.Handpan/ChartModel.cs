namespace RocoPilot.Handpan;

public sealed record MissingNote(int Pitch, double StartBeat, double Beats)
{
    public string NoteName => NoteNames.Of(Pitch);
}

public sealed record HandpanChart(
    IReadOnlyList<string> Lines,
    IReadOnlyList<MissingNote> Missing,
    double TotalBeats,
    double SecondsPerBeat)
{
    public string Text => string.Join(Environment.NewLine, Lines);

    public int MissingCount => Missing.Count;

    public double TotalSeconds => TotalBeats * SecondsPerBeat;
}
