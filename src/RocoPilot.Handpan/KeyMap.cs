namespace RocoPilot.Handpan;

public sealed record KeyMapEntry(string Note, string Key);

public sealed class KeyMap
{
    public const string DingNote = "A3";

    public static readonly IReadOnlyList<string> RingNotes =
        ["E4", "F4", "G4", "A4", "B4", "C5", "D5", "E5"];

    public static readonly IReadOnlyList<KeyMapEntry> DefaultEntries =
    [
        new(DingNote, "B"),
        new("E4", "F"),
        new("F4", "G"),
        new("G4", "H"),
        new("A4", "J"),
        new("B4", "K"),
        new("C5", "T"),
        new("D5", "Y"),
        new("E5", "U"),
    ];

    private readonly Dictionary<int, string> _byMidi;

    public KeyMap(IEnumerable<KeyMapEntry> entries)
    {
        _byMidi = [];
        foreach (var entry in entries)
        {
            if (!NoteNames.TryToMidi(entry.Note, out var midi) || string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            _byMidi[midi] = entry.Key.Trim();
        }
    }

    public IReadOnlyCollection<int> MidiNotes => _byMidi.Keys;

    public bool Contains(int midi) => _byMidi.ContainsKey(midi);

    public bool TryGet(int midi, out string key) => _byMidi.TryGetValue(midi, out key!);

    public IReadOnlyList<int> SortedMidiNotes
    {
        get
        {
            var notes = _byMidi.Keys.ToList();
            notes.Sort();
            return notes;
        }
    }
}
