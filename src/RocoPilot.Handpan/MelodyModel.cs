namespace RocoPilot.Handpan;

public sealed record PartProfile(
    int PartIndex,
    string Name,
    IReadOnlyList<MidiNote> Notes,
    double Polyphony,
    double AveragePitch,
    double MelodyScore)
{
    public int NoteCount => Notes.Count;

    public double Monophony => 1 - Polyphony;
}

public sealed record MelodySelection(
    IReadOnlyList<PartProfile> Profiles,
    PartProfile Part,
    IReadOnlyList<MidiNote> Line);

public sealed class MelodyPickException(string message) : Exception(message);
