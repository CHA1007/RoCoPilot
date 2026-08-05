using System.IO;
using System.Windows.Media.Imaging;
using RocoPilot.Capture;

namespace RocoPilot.Routing;

internal static class MinimapPngEncoder
{
    public static byte[] CropToPng(CapturedFrame frame, int x, int y, int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var source = frame.Pixels;

        for (var row = 0; row < height; row++)
        {
            var targetStart = row * stride;
            source.Slice((y + row) * frame.Stride + x * 4, stride).CopyTo(pixels.AsSpan(targetStart));
            for (var i = targetStart + 3; i < targetStart + stride; i += 4)
            {
                pixels[i] = 0xFF;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
