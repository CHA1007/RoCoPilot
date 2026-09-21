using System.IO;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan.Tests;

public class TempScoreTests
{
    [Fact]
    public void The_fixture_parses_into_a_melody_at_the_expected_beats()
    {
        using var score = TempScore.WithNotes((0, 72), (1, 74));

        var parsed = MidiParser.Parse(File.ReadAllBytes(score.Path));
        var notes = Assert.Single(parsed.Parts).Notes;

        Assert.Equal(120, parsed.Meta.Bpm, 6);
        Assert.Equal([72, 74], notes.Select(note => note.Pitch));
        Assert.Equal([0, 1], notes.Select(note => note.StartBeat));
        Assert.Equal([0.5, 0.5], notes.Select(note => note.EndBeat - note.StartBeat));
    }

    [Fact]
    public void Disposing_removes_the_file()
    {
        var score = TempScore.WithNotes((0, 72));
        var path = score.Path;

        score.Dispose();

        Assert.False(File.Exists(path));
    }
}
