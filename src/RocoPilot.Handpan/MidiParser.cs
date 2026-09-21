using System.Text;

namespace RocoPilot.Handpan;

public static class MidiParser
{
    private const int DefaultTempoUs = 500_000;
    private static readonly string[] SharpMajors = ["C", "G", "D", "A", "E", "B", "F#", "C#"];
    private static readonly string[] FlatMajors = ["C", "F", "Bb", "Eb", "Ab", "Db", "Gb"];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding? Gbk = CreateGbk();

    private static Encoding? CreateGbk()
    {
        try
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GBK");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static MidiScore Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || data[0] != (byte)'M' || data[1] != (byte)'T' || data[2] != (byte)'h' || data[3] != (byte)'d')
        {
            throw new MidiParseException("不是有效的 MIDI 文件（缺少 MThd 头）");
        }

        var headerLength = ReadUInt32(data, 4);
        if (headerLength < 6)
        {
            throw new MidiParseException("MThd 头长度非法");
        }

        var format = ReadUInt16(data, 8);
        if (format == 2)
        {
            throw new MidiParseException("不支持的 MIDI format 2（独立轨序列）");
        }

        var trackCount = ReadUInt16(data, 10);
        var division = ReadUInt16(data, 12);
        var position = 8 + (int)headerLength;

        var rawTracks = new List<RawTrack>();
        var tempos = new List<(int Tick, int Us)>();
        var keySignatures = new List<(int Sf, int Minor)>();
        var timeSignatures = new List<TimeSignature>();

        for (var t = 0; t < trackCount; t++)
        {
            if (position + 8 > data.Length || data[position] != (byte)'M' || data[position + 1] != (byte)'T'
                || data[position + 2] != (byte)'r' || data[position + 3] != (byte)'k')
            {
                throw new MidiParseException($"第 {t} 轨前缺少 MTrk 块头");
            }

            var trackLength = (int)ReadUInt32(data, position + 4);
            var bodyStart = position + 8;
            var bodyEnd = bodyStart + trackLength;
            if (bodyEnd > data.Length)
            {
                throw new MidiParseException($"第 {t} 轨数据越界");
            }

            var track = ParseTrack(data[bodyStart..bodyEnd], tempos, keySignatures, timeSignatures);
            if (track.Notes.Count > 0)
            {
                rawTracks.Add(track);
            }
            position = bodyEnd;
        }

        var tempoUs = PickMainTempo(tempos);
        var bpm = 60_000_000.0 / tempoUs;
        var tonic = TonicFromKeySignatures(keySignatures);
        var timeSignature = timeSignatures.Count > 0 ? timeSignatures[0] : new TimeSignature(4, 4);

        var parts = rawTracks
            .SelectMany(SplitByChannel)
            .Select(track => new MidiPart(track.Name,
                [.. track.Notes.Select(n => ToBeatNote(n, division, bpm))]))
            .ToList();

        return new MidiScore(parts, new MidiMeta(bpm, tonic, timeSignature));
    }

    private static RawTrack ParseTrack(
        ReadOnlySpan<byte> body,
        List<(int Tick, int Us)> tempos,
        List<(int Sf, int Minor)> keySignatures,
        List<TimeSignature> timeSignatures)
    {
        var name = string.Empty;
        var notes = new List<RawNote>();
        var opened = new Dictionary<(int Channel, int Pitch), Queue<int>>();
        var tick = 0;
        var index = 0;
        byte? running = null;

        while (index < body.Length)
        {
            tick += ReadVarLen(body, ref index);
            if (index >= body.Length)
            {
                throw new MidiParseException("轨道数据以 delta 结尾，缺少事件");
            }

            var status = body[index];
            if ((status & 0x80) != 0)
            {
                index++;
                running = status < 0xF0 ? status : null;
            }
            else
            {
                if (running is null)
                {
                    throw new MidiParseException("MIDI 数据损坏：running status 缺失");
                }
                status = running.Value;
            }

            if (status == 0xFF)
            {
                var type = ReadByte(body, ref index);
                var length = ReadVarLen(body, ref index);
                RequireRange(body, index + length, "元事件数据越界");
                var payload = body[index..(index + length)];
                index += length;
                if (type == 0x51 && length >= 3)
                {
                    tempos.Add((tick, (payload[0] << 16) | (payload[1] << 8) | payload[2]));
                }
                else if (type == 0x03 && length > 0 && name.Length == 0)
                {
                    name = DecodeText(payload);
                }
                else if (type == 0x59 && length >= 2)
                {
                    keySignatures.Add(((sbyte)payload[0], payload[1]));
                }
                else if (type == 0x58 && length >= 2)
                {
                    timeSignatures.Add(new TimeSignature(payload[0], 1 << payload[1]));
                }
            }
            else if (status is 0xF0 or 0xF7)
            {
                var length = ReadVarLen(body, ref index);
                index += length;
            }
            else
            {
                var channel = status & 0x0F;
                var kind = status & 0xF0;
                if (kind is 0xC0 or 0xD0)
                {
                    index++;
                }
                else if (kind == 0x90)
                {
                    RequireRange(body, index + 2, "音符事件数据越界");
                    var pitch = body[index];
                    var velocity = body[index + 1];
                    index += 2;
                    if (velocity > 0)
                    {
                        var key = (channel, pitch);
                        if (!opened.TryGetValue(key, out var queue))
                        {
                            opened[key] = queue = new Queue<int>();
                        }
                        queue.Enqueue(tick);
                    }
                    else
                    {
                        CloseNote(opened, notes, channel, pitch, tick);
                    }
                }
                else if (kind == 0x80)
                {
                    RequireRange(body, index + 2, "音符事件数据越界");
                    var pitch = body[index];
                    index += 2;
                    CloseNote(opened, notes, channel, pitch, tick);
                }
                else
                {
                    RequireRange(body, index + 2, "通道事件数据越界");
                    index += 2;
                }
            }
        }

        foreach (var ((channel, pitch), queue) in opened)
        {
            while (queue.Count > 0)
            {
                notes.Add(new RawNote(channel, pitch, queue.Dequeue(), tick));
            }
        }

        notes.Sort((a, b) => a.StartTick != b.StartTick ? a.StartTick.CompareTo(b.StartTick) : a.Pitch.CompareTo(b.Pitch));
        return new RawTrack(name, notes);
    }

    private static void CloseNote(
        Dictionary<(int Channel, int Pitch), Queue<int>> opened,
        List<RawNote> notes,
        int channel,
        int pitch,
        int endTick)
    {
        if (opened.TryGetValue((channel, pitch), out var queue) && queue.Count > 0)
        {
            notes.Add(new RawNote(channel, pitch, queue.Dequeue(), endTick));
        }
    }

    private static int PickMainTempo(List<(int Tick, int Us)> tempos)
    {
        if (tempos.Count == 0)
        {
            return DefaultTempoUs;
        }

        var ordered = tempos.OrderBy(t => t.Tick).ThenBy(t => t.Us).ToList();
        var bestSpan = -1;
        var bestUs = DefaultTempoUs;
        var previousTick = 0;
        var previousUs = DefaultTempoUs;
        foreach (var (tick, us) in ordered)
        {
            var span = tick - previousTick;
            if (span > bestSpan)
            {
                bestSpan = span;
                bestUs = previousUs;
            }
            previousTick = tick;
            previousUs = us;
        }
        return previousUs;
    }

    private static string? TonicFromKeySignatures(List<(int Sf, int Minor)> keySignatures)
    {
        if (keySignatures.Count == 0)
        {
            return null;
        }

        var (sf, minor) = keySignatures[0];
        if (Math.Abs(sf) > 7)
        {
            return null;
        }

        var major = sf >= 0 ? SharpMajors[sf] : FlatMajors[-sf];
        if (minor != 1)
        {
            return major;
        }

        if (!NoteNames.TryPitchClassOf(major, out var majorPitchClass))
        {
            return null;
        }

        return NoteNames.PitchClassName(majorPitchClass + 9);
    }

    private static IEnumerable<RawTrack> SplitByChannel(RawTrack track)
    {
        var counts = track.Notes.GroupBy(n => n.Channel).ToDictionary(g => g.Key, g => g.Count());
        var bigChannels = counts.Count(kv => kv.Value >= 4 && kv.Key != MidiChannels.Drums);
        if (bigChannels < 2)
        {
            yield return track;
            yield break;
        }

        foreach (var group in track.Notes.GroupBy(n => n.Channel).OrderBy(g => g.Key))
        {
            var notes = group.ToList();
            notes.Sort((a, b) => a.StartTick != b.StartTick ? a.StartTick.CompareTo(b.StartTick) : a.Pitch.CompareTo(b.Pitch));
            yield return new RawTrack($"{track.Name} ch{group.Key + 1}".Trim(), notes);
        }
    }

    private static MidiNote ToBeatNote(RawNote note, int division, double bpm)
    {
        if ((division & 0x8000) == 0)
        {
            if (division == 0)
            {
                throw new MidiParseException("division 为 0，无法换算拍");
            }
            return new MidiNote(note.Channel, note.Pitch, note.StartTick / (double)division, note.EndTick / (double)division);
        }

        var framesPerSecond = -(sbyte)(division >> 8);
        var ticksPerFrame = division & 0xFF;
        if (framesPerSecond == 0 || ticksPerFrame == 0)
        {
            throw new MidiParseException("SMPTE division 非法");
        }

        var beatsPerTick = 1.0 / (framesPerSecond * ticksPerFrame) * bpm / 60.0;
        return new MidiNote(note.Channel, note.Pitch, note.StartTick * beatsPerTick, note.EndTick * beatsPerTick);
    }

    private static string DecodeText(ReadOnlySpan<byte> payload)
    {
        try
        {
            return StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return Gbk?.GetString(payload) ?? Encoding.Latin1.GetString(payload);
        }
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int index)
    {
        RequireRange(data, index + 1, "数据越界");
        return data[index++];
    }

    private static int ReadVarLen(ReadOnlySpan<byte> data, ref int index)
    {
        var value = 0;
        byte current;
        do
        {
            current = ReadByte(data, ref index);
            value = (value << 7) | (current & 0x7F);
        }
        while ((current & 0x80) != 0);
        return value;
    }

    private static void RequireRange(ReadOnlySpan<byte> data, int end, string message)
    {
        if (end > data.Length)
        {
            throw new MidiParseException(message);
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private readonly record struct RawNote(int Channel, int Pitch, int StartTick, int EndTick);

    private sealed record RawTrack(string Name, List<RawNote> Notes);
}
