namespace RocoPilot.Handpan;

public sealed record PlaybackTiming(
    double HoldSeconds = 0.05,
    double ChordHoldSeconds = 0.06,
    double ChordStaggerSeconds = 0.012);

public sealed record KeyPress(string Key, double DownAtSeconds, double HoldSeconds)
{
    public double UpAtSeconds => DownAtSeconds + HoldSeconds;
}

public sealed record KeyEvent(string Key, double AtSeconds, bool IsDown);

public sealed record PlannedNote(double AtSeconds, IReadOnlyList<string> Keys, double HoldSeconds)
{
    public bool IsChord => Keys.Count > 1;
}

public sealed record PlaybackPlan(
    IReadOnlyList<PlannedNote> Notes,
    IReadOnlyList<KeyPress> Presses,
    IReadOnlyList<KeyEvent> KeyEvents,
    double SecondsPerBeat,
    double EndsAtSeconds,
    int MissingCount)
{
    public int NoteCount => Notes.Count;
}
