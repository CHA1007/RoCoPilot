namespace RocoPilot.Handpan;

public static class NoteNames
{
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static string Of(int midi)
    {
        var clamped = Math.Clamp(midi, 0, 127);
        return $"{SharpNames[clamped % 12]}{clamped / 12 - 1}";
    }

    public static string PitchClassName(int pitchClass) => SharpNames[((pitchClass % 12) + 12) % 12];

    public static bool TryPitchClassOf(string? name, out int pitchClass)
    {
        pitchClass = 0;
        return TryToMidi(name + "4", out var midi) && (pitchClass = midi % 12) >= 0;
    }

    public static bool TryToMidi(string? name, out int midi)
    {
        midi = 0;
        var text = name?.Trim();
        if (string.IsNullOrEmpty(text) || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        var letter = char.ToUpperInvariant(text[0]);
        if (letter is not ('A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G'))
        {
            return false;
        }

        var pitchClass = letter switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11,
        };

        var index = 1;
        if (index < text.Length && text[index] is '#' or 'b')
        {
            var sharp = text[index] == '#';
            var allowed = sharp
                ? letter is 'C' or 'D' or 'F' or 'G' or 'A'
                : letter is 'D' or 'E' or 'G' or 'A' or 'B';
            if (!allowed)
            {
                return false;
            }

            pitchClass = (pitchClass + (sharp ? 1 : 11)) % 12;
            index++;
        }

        if (!int.TryParse(text.AsSpan(index), out var octave))
        {
            return false;
        }

        var value = (octave + 1) * 12 + pitchClass;
        if (value is < 0 or > 127)
        {
            return false;
        }

        midi = value;
        return true;
    }
}
