using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RocoPilot.Capture;

namespace RocoPilot.Shell.Pages;

internal static class PixelPaint
{
    internal static void ForceOpaque(byte[] buffer, int length)
    {
        for (var i = 3; i < length; i += 4)
        {
            buffer[i] = 0xFF;
        }
    }

    internal static void DrawRect(
        byte[] buffer, int width, int height,
        float x1, float y1, float x2, float y2,
        byte b, byte g, byte r, int thickness)
    {
        var left = Math.Clamp((int)x1, 0, width - 1);
        var right = Math.Clamp((int)x2, 0, width - 1);
        var top = Math.Clamp((int)y1, 0, height - 1);
        var bottom = Math.Clamp((int)y2, 0, height - 1);
        if (left > right || top > bottom)
        {
            return;
        }

        for (var t = 0; t < thickness; t++)
        {
            var topRow = Math.Clamp(top + t, 0, height - 1);
            var bottomRow = Math.Clamp(bottom - t, 0, height - 1);
            for (var x = left; x <= right; x++)
            {
                SetPixel(buffer, topRow * width + x, b, g, r);
                SetPixel(buffer, bottomRow * width + x, b, g, r);
            }

            var leftCol = Math.Clamp(left + t, 0, width - 1);
            var rightCol = Math.Clamp(right - t, 0, width - 1);
            for (var y = top; y <= bottom; y++)
            {
                SetPixel(buffer, y * width + leftCol, b, g, r);
                SetPixel(buffer, y * width + rightCol, b, g, r);
            }
        }
    }

    internal static void DrawCrosshair(
        byte[] buffer, int width, int height,
        int cx, int cy, int radius, byte b, byte g, byte r)
    {
        for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
        {
            SetPixel(buffer, cy * width + x, b, g, r);
        }

        for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); y++)
        {
            SetPixel(buffer, y * width + cx, b, g, r);
        }
    }

    private static void SetPixel(byte[] buffer, int pixelIndex, byte b, byte g, byte r)
    {
        var offset = pixelIndex * 4;
        buffer[offset] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = 0xFF;
    }
}

internal sealed class FrameBlitter
{
    private WriteableBitmap? _bitmap;
    private byte[] _buffer = [];

    internal byte[] Buffer => _buffer;

    internal bool Prepare(CapturedFrame frame)
    {
        if (frame.Pixels.IsEmpty)
        {
            return false;
        }

        var length = frame.Pixels.Length;
        if (_buffer.Length < length)
        {
            _buffer = new byte[length];
        }

        frame.Pixels.CopyTo(_buffer);
        PixelPaint.ForceOpaque(_buffer, length);
        return true;
    }

    internal void Blit(Image target, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            target.Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _buffer, width * 4, 0);
    }
}
