namespace RocoPilot.Handpan;

public static class MidiChannels
{
    public const int Drums = 9;
}

public sealed record MidiNote(int Channel, int Pitch, double StartBeat, double EndBeat);

public sealed record MidiPart(string Name, IReadOnlyList<MidiNote> Notes);

public sealed record TimeSignature(int Numerator, int Denominator)
{
    public double BeatsPerBar => Numerator is >= 1 and <= 12 && Denominator is 1 or 2 or 4 or 8 or 16
        ? Numerator * 4.0 / Denominator
        : 4;
}

public sealed record MidiMeta(double Bpm, string? Tonic, TimeSignature TimeSignature);

public sealed record MidiScore(IReadOnlyList<MidiPart> Parts, MidiMeta Meta);

public sealed class MidiParseException(string message) : Exception(message);
