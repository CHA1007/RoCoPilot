namespace RocoPilot.Handpan;

public static class BeatGrid
{
    public const int UnitsPerBeat = 48;

    public static long ToUnits(double beats) => (long)Math.Round(beats * UnitsPerBeat);

    public static double ToBeats(long units) => (double)units / UnitsPerBeat;
}
