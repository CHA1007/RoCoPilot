namespace RocoPilot.Handpan;

public static class MelodyPicker
{
    private const double MonophonicPolyphonyLimit = 0.1;
    private const double PolyphonyPenaltyScale = 3;
    private const double PitchBonusReference = 60;
    private const double PitchBonusScale = 0.8;
    private const double HighRegisterPitch = 64;
    private const double UpperMiddleRegisterPitch = 58;
    private const double LowerMiddleRegisterPitch = 50;

    public static MelodySelection Pick(MidiScore score)
    {
        var profiles = Profiles(score);
        if (profiles.Count == 0)
        {
            throw new MelodyPickException("MIDI 里没有可用声部（全是鼓？）");
        }

        var part = profiles[0];
        foreach (var candidate in profiles)
        {
            if (candidate.MelodyScore > part.MelodyScore)
            {
                part = candidate;
            }
        }

        return new MelodySelection(profiles, part, MelodyLine(part));
    }

    public static IReadOnlyList<PartProfile> Profiles(MidiScore score)
    {
        var profiles = new List<PartProfile>();
        for (var partIndex = 0; partIndex < score.Parts.Count; partIndex++)
        {
            var profile = Profile(score.Parts[partIndex], partIndex);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    private static PartProfile? Profile(MidiPart part, int partIndex)
    {
        var notes = part.Notes
            .Where(note => note.Channel != MidiChannels.Drums)
            .OrderBy(note => note.StartBeat)
            .ThenBy(note => note.Pitch)
            .ToList();
        if (notes.Count == 0)
        {
            return null;
        }

        var polyphony = Polyphony(notes);
        var averagePitch = notes.Average(note => note.Pitch);
        return new PartProfile(partIndex, part.Name, notes, polyphony, averagePitch,
            MelodyScore(notes.Count, polyphony, averagePitch));
    }

    private static IReadOnlyList<MidiNote> MelodyLine(PartProfile part) =>
        part.Polyphony < MonophonicPolyphonyLimit ? part.Notes : TopVoice.Extract(part.Notes);

    private static double Polyphony(IReadOnlyList<MidiNote> notes)
    {
        var overlapped = 0;
        var latestEnd = double.MinValue;
        foreach (var note in notes)
        {
            if (note.StartBeat < latestEnd)
            {
                overlapped++;
            }

            latestEnd = Math.Max(latestEnd, note.EndBeat);
        }

        return (double)overlapped / notes.Count;
    }

    private static double MelodyScore(int noteCount, double polyphony, double averagePitch) =>
        noteCount * RegisterWeight(averagePitch) * (1 - Math.Min(1, polyphony * PolyphonyPenaltyScale))
        + (averagePitch - PitchBonusReference) * PitchBonusScale;

    private static double RegisterWeight(double averagePitch) => averagePitch switch
    {
        >= HighRegisterPitch => 1.0,
        >= UpperMiddleRegisterPitch => 0.7,
        >= LowerMiddleRegisterPitch => 0.35,
        _ => 0.08,
    };
}
