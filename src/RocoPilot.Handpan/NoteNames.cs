using System.Globalization;
using System.Text.RegularExpressions;

namespace RocoPilot.Handpan;

public static partial class NoteNames
{
    private static readonly string[] Names =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly Dictionary<int, int> SemitoneToDegree = new()
    {
        [0] = 1, [2] = 2, [4] = 3, [5] = 4, [7] = 5, [9] = 6, [11] = 7,
    };

    public static string Of(int midi) => $"{Names[Wrap(midi)]}{midi / 12 - 1}";

    public static int ToMidi(string name)
    {
        var m = NameRegex().Match(name.Trim());
        if (!m.Success)
        {
            throw new FormatException($"无法识别的音名: {name}");
        }

        var baseIndex = Array.IndexOf(Names, m.Groups[1].Value.ToUpperInvariant());
        var accidental = m.Groups[2].Value;
        var shift = accidental == "#" ? 1 : accidental.Length > 0 ? -1 : 0;
        return 12 * (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) + 1) + baseIndex + shift;
    }

    public static bool TryToMidi(string name, out int midi)
    {
        try
        {
            midi = ToMidi(name);
            return true;
        }
        catch (FormatException)
        {
            midi = 0;
            return false;
        }
    }

    public static string? DegreeZh(int midi)
    {
        var diff = midi - 60;
        var semi = ((diff % 12) + 12) % 12;
        if (!SemitoneToDegree.TryGetValue(semi, out var degree))
        {
            return null;
        }

        var octaves = (diff - semi) / 12;
        return octaves > 0 ? $"高音{degree}" : octaves < 0 ? $"低音{degree}" : $"中音{degree}";
    }

    private static int Wrap(int midi) => ((midi % 12) + 12) % 12;

    [GeneratedRegex(@"^([A-Ga-g])([#bB]?)(-?\d+)$")]
    private static partial Regex NameRegex();
}
