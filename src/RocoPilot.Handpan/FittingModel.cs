namespace RocoPilot.Handpan;

public sealed record MelodyFit(
    IReadOnlyList<MidiNote> Notes,
    int Semitones,
    double ExactCoverage,
    int FoldedCount,
    int SnappedCount,
    IReadOnlyList<MidiNote> MissingNotes)
{
    public int MissingCount => MissingNotes.Count;

    public bool IsComplete => MissingNotes.Count == 0;
}
