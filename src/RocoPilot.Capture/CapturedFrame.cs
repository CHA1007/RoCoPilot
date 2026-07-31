using System.Buffers;

namespace RocoPilot.Capture;

public sealed class CapturedFrame : IDisposable
{
    private byte[]? _rented;

    internal CapturedFrame(byte[] rented, int width, int height, long sequence, DateTimeOffset capturedAt)
    {
        _rented = rented;
        Width = width;
        Height = height;
        Sequence = sequence;
        CapturedAt = capturedAt;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    public long Sequence { get; }

    public DateTimeOffset CapturedAt { get; }

    public ReadOnlySpan<byte> Pixels => _rented is null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_rented, 0, Stride * Height);

    public void Dispose()
    {
        var rented = Interlocked.Exchange(ref _rented, null);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
