using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RocoPilot.Loop;

public sealed class WpfSceneImageEncoder : ISceneImageEncoder
{
    private const double LabelFontSize = 14;
    private const double CrosshairRadius = 20;

    public byte[] EncodeKeyframe(FrameSnapshot frame) => EncodePng(MakeBitmap(frame));

    public byte[] EncodeOverlay(FrameSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bitmap = MakeBitmap(frame);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(bitmap, new Rect(0, 0, frame.Width, frame.Height));

            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(80, 230, 80)), 2);
            boxPen.Freeze();
            var labelTypeface = new Typeface("Segoe UI");
            foreach (var box in frame.Detections)
            {
                context.DrawRectangle(null, boxPen, new Rect(box.X1, box.Y1, box.Width, box.Height));

                var text = new FormattedText(
                    $"{box.ClassName} {box.Confidence:0.00}",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    labelTypeface, LabelFontSize, Brushes.White, pixelsPerDip: 1.0);
                var labelY = box.Y1 - text.Height >= 0 ? box.Y1 - text.Height : box.Y1;
                var labelX = Math.Max(0, Math.Min(box.X1, frame.Width - text.Width));
                context.DrawRectangle(Brushes.Black, null, new Rect(labelX, labelY, text.Width + 4, text.Height));
                context.DrawText(text, new Point(labelX + 2, labelY));
            }

            var crossPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 60, 60)), 1);
            crossPen.Freeze();
            var centerX = frame.Width / 2.0;
            var centerY = frame.Height / 2.0;
            context.DrawLine(crossPen, new Point(centerX - CrosshairRadius, centerY), new Point(centerX + CrosshairRadius, centerY));
            context.DrawLine(crossPen, new Point(centerX, centerY - CrosshairRadius), new Point(centerX, centerY + CrosshairRadius));
        }

        var target = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return EncodePng(target);
    }

    private static BitmapSource MakeBitmap(FrameSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stride = frame.Width * 4;
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < stride * frame.Height)
        {
            throw new ArgumentException(
                $"快照帧尺寸无效（{frame.Width}x{frame.Height}，字节 {frame.Pixels.Length}）", nameof(frame));
        }

        var pixels = (byte[])frame.Pixels.Clone();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF;
        }

        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
