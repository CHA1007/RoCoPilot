namespace RocoPilot.Routing;

public sealed record MinimapSnapshotRegion(double X, double Y, double Width, double Height)
{
    public static readonly MinimapSnapshotRegion TopLeft = new(0.01, 0.02, 0.18, 0.30);

    public (int X, int Y, int Width, int Height) Resolve(int frameWidth, int frameHeight)
    {
        var x = Math.Clamp((int)Math.Round(frameWidth * X), 0, frameWidth);
        var y = Math.Clamp((int)Math.Round(frameHeight * Y), 0, frameHeight);
        var width = Math.Clamp((int)Math.Round(frameWidth * Width), 0, frameWidth - x);
        var height = Math.Clamp((int)Math.Round(frameHeight * Height), 0, frameHeight - y);
        return (x, y, width, height);
    }
}
