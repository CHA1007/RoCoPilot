using System.IO;

namespace RocoPilot.Tools.Handpan.Tests;

internal sealed class TempScore : IDisposable
{
    private const int Division = 480;

    private TempScore(string path) => Path = path;

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public static TempScore WithNotes(params (double Beat, int Pitch)[] notes)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rocopilot-handpan-{Guid.NewGuid():N}.mid");
        File.WriteAllBytes(path, Build(notes));
        return new TempScore(path);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Build((double Beat, int Pitch)[] notes)
    {
        var body = new List<byte> { 0 };
        body.AddRange(Meta(0x51, 0x07, 0xA1, 0x20));
        var tick = 0;
        foreach (var (beat, pitch) in notes)
        {
            var onset = (int)Math.Round(beat * Division);
            body.AddRange(VarLen(onset - tick));
            body.AddRange([0x90, (byte)pitch, 80]);
            body.AddRange(VarLen(Division / 2));
            body.AddRange([0x80, (byte)pitch, 0]);
            tick = onset + Division / 2;
        }

        body.AddRange(VarLen(0));
        body.AddRange(Meta(0x2F));
        return
        [
            (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1,
            (byte)(Division >> 8), (byte)(Division & 0xFF),
            (byte)'M', (byte)'T', (byte)'r', (byte)'k',
            (byte)(body.Count >> 24), (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count,
            .. body,
        ];
    }

    private static byte[] Meta(byte type, params byte[] payload) =>
        [0xFF, type, (byte)payload.Length, .. payload];

    private static byte[] VarLen(int value)
    {
        var buffer = new List<byte>();
        var rest = Math.Max(0, value);
        do
        {
            buffer.Insert(0, (byte)(rest & 0x7F));
            rest >>= 7;
        }
        while (rest > 0);
        for (var index = 0; index < buffer.Count - 1; index++)
        {
            buffer[index] |= 0x80;
        }

        return [.. buffer];
    }
}

internal sealed class TempScoreLibrary : IDisposable
{
    private readonly string _dir;

    private TempScoreLibrary(string dir) => (_dir, Store) = (dir, new HandpanScoreStore(dir));

    public HandpanScoreStore Store { get; }

    public string Add(TempScore score) => Store.Import(score.Path);

    public static TempScoreLibrary Create()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rocopilot-handpan-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new TempScoreLibrary(dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
