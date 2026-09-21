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

public sealed record MusicKey(string Tonic, bool IsMinor)
{
    public string? RelativeMajor => IsMinor && NoteNames.TryPitchClassOf(Tonic, out var pitchClass)
        ? NoteNames.PitchClassName(pitchClass + 3)
        : null;

    public MusicKey Transposed(int semitones)
    {
        if (semitones % 12 == 0 || !NoteNames.TryPitchClassOf(Tonic, out var pitchClass))
        {
            return this;
        }

        return this with { Tonic = NoteNames.PitchClassName(pitchClass + semitones) };
    }
}

public sealed record MidiMeta(double Bpm, MusicKey? Key, TimeSignature TimeSignature);

public sealed record MidiScore(IReadOnlyList<MidiPart> Parts, MidiMeta Meta);

public sealed class MidiParseException(string message) : Exception(message);
