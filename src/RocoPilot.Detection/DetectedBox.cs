namespace RocoPilot.Detection;

public sealed record DetectedBox(int ClassIndex, string ClassName, float Confidence, float X1, float Y1, float X2, float Y2)
{
    public float CenterX => (X1 + X2) / 2f;

    public float CenterY => (Y1 + Y2) / 2f;

    public float Width => X2 - X1;

    public float Height => Y2 - Y1;

    public float Area => Math.Max(0f, Width) * Math.Max(0f, Height);
}
