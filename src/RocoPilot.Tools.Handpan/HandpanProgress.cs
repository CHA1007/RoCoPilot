namespace RocoPilot.Tools.Handpan;

public sealed record HandpanProgress(double ElapsedSeconds, double TotalSeconds, int Round)
{
    public double Ratio => TotalSeconds <= 0 ? 0 : Math.Clamp(ElapsedSeconds / TotalSeconds, 0, 1);

    public int RoundNumber => Round + 1;

    public bool IsLooping => Round > 0;
}
