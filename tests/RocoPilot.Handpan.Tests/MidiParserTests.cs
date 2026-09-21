using System.Text;
using RocoPilot.Handpan;

namespace RocoPilot.Handpan.Tests;

file static class MidiBytes
{
    public static byte[] VarLen(int value)
    {
        var buffer = new List<byte>();
        var rest = value;
        do
        {
            buffer.Insert(0, (byte)(rest & 0x7F));
            rest >>= 7;
        } while (rest > 0);
        for (var i = 0; i < buffer.Count - 1; i++)
        {
            buffer[i] |= 0x80;
        }
        return [.. buffer];
    }

    public static byte[] File(int format, int division, params TrackBuilder[] tracks)
    {
        var bytes = new List<byte> { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6,
            (byte)(format >> 8), (byte)format, 0, (byte)tracks.Length,
            (byte)(division >> 8), (byte)division };
        foreach (var track in tracks)
        {
            bytes.AddRange(track.ToBytes());
        }
        return [.. bytes];
    }
}

file sealed class TrackBuilder
{
    private readonly List<byte> _events = [];
    private int _tick;
    private bool _deltaPending = true;

    public TrackBuilder At(int tick)
    {
        if (tick < _tick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "事件时间不能倒退");
        }

        _events.AddRange(MidiBytes.VarLen(tick - _tick));
        _tick = tick;
        _deltaPending = false;
        return this;
    }

    private TrackBuilder EnsureDelta()
    {
        if (_deltaPending)
        {
            _events.Add(0);
            _deltaPending = false;
        }
        return this;
    }

    private void EventWritten()
    {
        _deltaPending = true;
    }

    public TrackBuilder NoteOn(int channel, int pitch, int velocity = 80)
    {
        EnsureDelta();
        _events.Add((byte)(0x90 | channel));
        _events.Add((byte)pitch);
        _events.Add((byte)velocity);
        EventWritten();
        return this;
    }

    public TrackBuilder NoteOff(int channel, int pitch)
    {
        EnsureDelta();
        _events.Add((byte)(0x80 | channel));
        _events.Add((byte)pitch);
        _events.Add(0);
        EventWritten();
        return this;
    }

    public TrackBuilder Meta(byte type, params byte[] payload)
    {
        EnsureDelta();
        _events.Add(0xFF);
        _events.Add(type);
        _events.AddRange(MidiBytes.VarLen(payload.Length));
        _events.AddRange(payload);
        EventWritten();
        return this;
    }

    public TrackBuilder Name(string name)
    {
        return Meta(0x03, Encoding.UTF8.GetBytes(name));
    }

    public TrackBuilder Tempo(int microseconds)
    {
        return Meta(0x51, [(byte)(microseconds >> 16), (byte)(microseconds >> 8), (byte)microseconds]);
    }

    public TrackBuilder KeySignature(int sf, int minor)
    {
        return Meta(0x59, [(byte)sf, (byte)minor]);
    }

    public TrackBuilder TimeSignature(int numerator, int denominatorPower)
    {
        return Meta(0x58, [(byte)numerator, (byte)denominatorPower]);
    }

    public TrackBuilder SysEx(params byte[] payload)
    {
        EnsureDelta();
        _events.Add(0xF0);
        _events.AddRange(MidiBytes.VarLen(payload.Length));
        _events.AddRange(payload);
        EventWritten();
        return this;
    }

    public TrackBuilder ProgramChange(int channel, int program)
    {
        EnsureDelta();
        _events.Add((byte)(0xC0 | channel));
        _events.Add((byte)program);
        EventWritten();
        return this;
    }

    public TrackBuilder Raw(params byte[] bytes)
    {
        _events.AddRange(bytes);
        return this;
    }

    public byte[] ToBytes()
    {
        var body = _events.ToArray();
        return [(byte)'M', (byte)'T', (byte)'r', (byte)'k',
            (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length,
            .. body];
    }
}

public class MidiParserTests
{
    private static MidiScore Parse(byte[] data) => MidiParser.Parse(data);

    [Fact]
    public void Rejects_missing_header()
    {
        Assert.Throws<MidiParseException>(() => Parse([1, 2, 3]));
    }

    [Fact]
    public void Rejects_format_2()
    {
        var data = MidiBytes.File(2, 480);
        Assert.Throws<MidiParseException>(() => Parse(data));
    }

    [Fact]
    public void Single_track_notes_convert_ticks_to_beats()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).NoteOn(0, 60).At(480).NoteOff(0, 60)
                .At(480).NoteOn(0, 62).At(960).NoteOff(0, 62));

        var score = Parse(data);

        var part = Assert.Single(score.Parts);
        Assert.Equal(string.Empty, part.Name);
        Assert.Collection(part.Notes,
            n => Assert.Equal(new MidiNote(0, 60, 0, 1), n),
            n => Assert.Equal(new MidiNote(0, 62, 1, 2), n));
    }

    [Fact]
    public void Note_on_with_zero_velocity_closes_the_note()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).NoteOn(0, 60)
                .At(240).Raw(0x90, 60, 0));

        var score = Parse(data);

        Assert.Equal(new MidiNote(0, 60, 0, 0.5), Assert.Single(Assert.Single(score.Parts).Notes));
    }

    [Fact]
    public void Unclosed_notes_close_at_track_end()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).NoteOn(0, 60)
                .At(96).NoteOn(0, 64)
                .At(192).ProgramChange(0, 5));

        var score = Parse(data);

        Assert.Collection(Assert.Single(score.Parts).Notes,
            n => Assert.Equal(new MidiNote(0, 60, 0, 0.4), n),
            n => Assert.Equal(new MidiNote(0, 64, 0.2, 0.4), n));
    }

    [Fact]
    public void Running_status_carries_note_events()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).NoteOn(0, 60)
                .At(120).Raw(62, 80)
                .At(240).Raw(64, 80)
                .At(480).NoteOff(0, 60).NoteOff(0, 62).NoteOff(0, 64));

        var score = Parse(data);

        var notes = Assert.Single(score.Parts).Notes;
        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal(0, n.Channel));
        Assert.Equal([0, 0.25, 0.5], notes.Select(n => n.StartBeat));
    }

    [Fact]
    public void Tempo_meta_sets_bpm()
    {
        var data = MidiBytes.File(1, 480,
            new TrackBuilder().At(0).Tempo(600_000),
            new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Equal(100, Parse(data).Meta.Bpm);
    }

    [Fact]
    public void Main_tempo_is_the_longest_span()
    {
        var data = MidiBytes.File(1, 480,
            new TrackBuilder()
                .At(0).Tempo(500_000)
                .At(480).Tempo(1_000_000),
            new TrackBuilder()
                .At(0).NoteOn(0, 60)
                .At(1920).NoteOff(0, 60));

        Assert.Equal(60, Parse(data).Meta.Bpm);
    }

    [Fact]
    public void Missing_tempo_defaults_to_120()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Equal(120, Parse(data).Meta.Bpm);
    }

    [Theory]
    [InlineData(0, 0, "C")]
    [InlineData(3, 0, "A")]
    [InlineData(-2, 0, "Bb")]
    [InlineData(0, 1, "A")]
    [InlineData(2, 1, "B")]
    public void Key_signature_meta_derives_tonic(int sf, int minor, string expected)
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder().At(0).KeySignature(sf, minor).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Equal(expected, Parse(data).Meta.Tonic);
    }

    [Fact]
    public void Missing_key_signature_leaves_tonic_null()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Null(Parse(data).Meta.Tonic);
    }

    [Fact]
    public void Time_signature_meta_is_parsed()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder().At(0).TimeSignature(3, 2).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Equal(new TimeSignature(3, 4), Parse(data).Meta.TimeSignature);
    }

    [Fact]
    public void Time_signature_defaults_to_4_4()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Equal(new TimeSignature(4, 4), Parse(data).Meta.TimeSignature);
    }

    [Fact]
    public void Format_1_tracks_with_notes_become_named_parts()
    {
        var data = MidiBytes.File(1, 480,
            new TrackBuilder().At(0).Tempo(500_000).Name("指挥轨"),
            new TrackBuilder().At(0).Name("旋律").NoteOn(0, 60).At(480).NoteOff(0, 60),
            new TrackBuilder().At(0).Name("伴奏").NoteOn(1, 55).At(480).NoteOff(1, 55));

        var score = Parse(data);

        Assert.Equal(["旋律", "伴奏"], score.Parts.Select(p => p.Name));
    }

    [Fact]
    public void Multi_channel_track_splits_into_channel_parts()
    {
        var builder = new TrackBuilder().At(0).Name("合轨");
        for (var i = 0; i < 4; i++)
        {
            builder.At(i * 96).NoteOn(0, 60 + i).NoteOn(1, 72 + i);
            builder.At(i * 96 + 48).NoteOff(0, 60 + i).NoteOff(1, 72 + i);
        }
        var data = MidiBytes.File(0, 480, builder);

        var score = Parse(data);

        Assert.Equal(["合轨 ch1", "合轨 ch2"], score.Parts.Select(p => p.Name));
        Assert.Equal(4, score.Parts[0].Notes.Count);
        Assert.Equal(4, score.Parts[1].Notes.Count);
    }

    [Fact]
    public void Small_channel_counts_do_not_split()
    {
        var builder = new TrackBuilder();
        for (var i = 0; i < 3; i++)
        {
            builder.At(i * 96).NoteOn(0, 60 + i).NoteOn(1, 72 + i);
            builder.At(i * 96 + 48).NoteOff(0, 60 + i).NoteOff(1, 72 + i);
        }
        var data = MidiBytes.File(0, 480, builder);

        var score = Parse(data);

        Assert.Single(score.Parts);
        Assert.Equal(6, score.Parts[0].Notes.Count);
    }

    [Fact]
    public void Drum_channel_does_not_trigger_split_but_stays_in_part()
    {
        var builder = new TrackBuilder();
        for (var i = 0; i < 4; i++)
        {
            builder.At(i * 96).NoteOn(0, 60 + i);
            builder.At(i * 96 + 48).NoteOff(0, 60 + i);
        }
        for (var i = 0; i < 8; i++)
        {
            builder.At(384 + i * 48).NoteOn(9, 36);
            builder.At(384 + i * 48 + 24).NoteOff(9, 36);
        }
        var data = MidiBytes.File(0, 480, builder);

        var score = Parse(data);

        var part = Assert.Single(score.Parts);
        Assert.Equal(12, part.Notes.Count);
        Assert.Equal(8, part.Notes.Count(n => n.Channel == 9));
    }

    [Fact]
    public void SysEx_and_program_change_are_skipped()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).SysEx(0x7D, 1, 2, 3)
                .At(0).ProgramChange(0, 40)
                .At(0).NoteOn(0, 60)
                .At(480).NoteOff(0, 60));

        var score = Parse(data);

        Assert.Equal(new MidiNote(0, 60, 0, 1), Assert.Single(Assert.Single(score.Parts).Notes));
    }

    [Fact]
    public void Smpte_division_converts_through_bpm()
    {
        var smpte = (0x100 - 25) << 8 | 40;
        var data = MidiBytes.File(0, smpte,
            new TrackBuilder().At(0).NoteOn(0, 60).At(500).NoteOff(0, 60));

        var score = Parse(data);

        var note = Assert.Single(Assert.Single(score.Parts).Notes);
        Assert.Equal(0, note.StartBeat, 5);
        Assert.Equal(1, note.EndBeat, 5);
    }

    [Fact]
    public void Truncated_track_data_throws()
    {
        var data = MidiBytes.File(0, 480, new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));
        var cut = data[..^3];

        Assert.Throws<MidiParseException>(() => Parse(cut));
    }

    [Fact]
    public void Notes_are_sorted_by_start_then_pitch()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).NoteOn(0, 72).NoteOn(0, 60)
                .At(48).NoteOn(0, 55)
                .At(96).NoteOff(0, 72).NoteOff(0, 60).NoteOff(0, 55));

        var score = Parse(data);

        Assert.Equal([(0, 60), (0, 72), (0.1, 55)],
            Assert.Single(score.Parts).Notes.Select(n => (n.StartBeat, n.Pitch)));
    }

    [Fact]
    public void Rejects_zero_ppq_division()
    {
        var data = MidiBytes.File(0, 0,
            new TrackBuilder().At(0).NoteOn(0, 60).At(480).NoteOff(0, 60));

        Assert.Throws<MidiParseException>(() => Parse(data));
    }

    [Fact]
    public void Rejects_short_header()
    {
        var data = new byte[]
        {
            (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 4, 0, 0, 0, 1, 0x01, 0xE0,
        };

        Assert.Throws<MidiParseException>(() => Parse(data));
    }

    [Fact]
    public void Rejects_missing_track_chunk()
    {
        var data = new byte[]
        {
            (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
        };

        Assert.Throws<MidiParseException>(() => Parse(data));
    }

    [Fact]
    public void Track_names_fall_back_to_gbk_when_utf8_fails()
    {
        var data = MidiBytes.File(0, 480,
            new TrackBuilder()
                .At(0).Meta(0x03, 0xB8, 0xD6)
                .NoteOn(0, 60).At(480).NoteOff(0, 60));

        var score = Parse(data);

        Assert.Equal("钢", Assert.Single(score.Parts).Name);
    }
}
