using System.Globalization;
using System.Text.RegularExpressions;

namespace RocoPilot.Handpan;

public sealed record ParsedScore(IReadOnlyList<HandpanEvent> Events, ScoreMeta Meta);

public sealed class ScoreParseException(string message) : Exception(message);

public static partial class ScoreParser
{
    private static readonly int[] DegreeOffsets = [0, 2, 4, 5, 7, 9, 11];

    public static ParsedScore Parse(string text)
    {
        var meta = ScoreMeta.Default();
        var events = new List<HandpanEvent>();
        var tonic = NoteNames.ToMidi("C4");

        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('%'))
            {
                continue;
            }

            var keyMatch = KeyRegex().Match(line);
            if (keyMatch.Success)
            {
                meta = meta with { Key = keyMatch.Groups[1].Value.ToUpperInvariant() + keyMatch.Groups[2].Value };
                tonic = NoteNames.ToMidi(meta.Key + "4");
                continue;
            }

            var bpmMatch = BpmRegex().Match(line);
            if (bpmMatch.Success)
            {
                meta = meta with
                {
                    Bpm = double.Parse(bpmMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                };
                continue;
            }

            var titleMatch = TitleRegex().Match(line);
            if (titleMatch.Success)
            {
                meta = meta with { Title = titleMatch.Groups[2].Value.Trim() };
                continue;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "|")
                {
                    events.Add(HandpanEvent.Bar());
                    continue;
                }

                var noteMatch = NoteRegex().Match(token);
                if (noteMatch.Success)
                {
                    var accidental = noteMatch.Groups["acc"].Value == "#" ? 1
                        : noteMatch.Groups["acc"].Value.Length > 0 ? -1 : 0;
                    var degree = noteMatch.Groups["deg"].Value[0] - '1';
                    var octaves = noteMatch.Groups["up"].Value.Length - noteMatch.Groups["down"].Value.Length;
                    var midi = tonic + DegreeOffsets[degree] + accidental + 12 * octaves;
                    events.Add(HandpanEvent.Note(midi, DurationToBeats(noteMatch.Groups["dur"].Value),
                        token, NoteNames.Of(midi)));
                    continue;
                }

                var restMatch = RestRegex().Match(token);
                if (restMatch.Success)
                {
                    events.Add(HandpanEvent.Rest(DurationToBeats(restMatch.Groups["dur"].Value)));
                    continue;
                }

                var pitchMatch = PitchRegex().Match(token);
                if (pitchMatch.Success)
                {
                    events.Add(HandpanEvent.Note(NoteNames.ToMidi(pitchMatch.Groups["name"].Value),
                        PitchDurationToBeats(pitchMatch.Groups["dur"].Value),
                        token, pitchMatch.Groups["name"].Value.ToUpperInvariant()));
                    continue;
                }

                throw new ScoreParseException($"无法解析的记号: {token}（所在行: {line}）");
            }
        }

        return new ParsedScore(events, meta);
    }

    private static double DurationToBeats(string duration)
    {
        if (duration.Length == 0)
        {
            return 1;
        }

        if (duration.StartsWith(':'))
        {
            return double.Parse(duration[1..], CultureInfo.InvariantCulture);
        }

        var beats = duration.Contains('_') ? 0.5 * Math.Pow(0.5, CountChar(duration, '_') - 1) : 1.0;
        return beats + CountChar(duration, '-');
    }

    private static double PitchDurationToBeats(string duration)
    {
        if (duration.Length == 0)
        {
            return 1;
        }

        if (!duration.StartsWith('.'))
        {
            return double.Parse(duration, CultureInfo.InvariantCulture);
        }

        return 1.0 + duration.Length - 1;
    }

    private static int CountChar(string text, char c) => text.Count(ch => ch == c);

    [GeneratedRegex(@"^1\s*=\s*([A-Ga-g])([#b]?)$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^bpm\s*[=:：]\s*(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex BpmRegex();

    [GeneratedRegex(@"^(title|曲名|标题)\s*[=:]?\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^(?<acc>[#b]?)(?<deg>[1-7])(?<up>'*)(?<down>,*)(?<dur>[-_]+|:\d+(?:\.\d+)?)?$")]
    private static partial Regex NoteRegex();

    [GeneratedRegex(@"^0(?<dur>[-_]+|:\d+(?:\.\d+)?)?$")]
    private static partial Regex RestRegex();

    [GeneratedRegex(@"^(?<name>[A-Ga-g][#b]?-?\d+)(?<dur>[._\d]*)$")]
    private static partial Regex PitchRegex();
}
