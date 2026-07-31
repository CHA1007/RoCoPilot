namespace RocoPilot.Loop;

public sealed class CalibrationCache
{
    private readonly SortedList<int, double> _buckets = [];

    public bool HasValue => _buckets.Count > 0;

    public IReadOnlyDictionary<int, double> Buckets => new Dictionary<int, double>(_buckets);

    public double? PpcFor(double commandMagnitude)
    {
        if (_buckets.Count == 0)
        {
            return null;
        }

        var magnitude = Math.Abs(commandMagnitude);
        var bestKey = _buckets.Keys[0];
        var bestDistance = Math.Abs(magnitude - bestKey);
        foreach (var key in _buckets.Keys)
        {
            var distance = Math.Abs(magnitude - key);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestKey = key;
            }
        }

        return _buckets[bestKey];
    }

    public void Store(int magnitude, double pixelsPerCount)
    {
        if (magnitude <= 0) throw new ArgumentOutOfRangeException(nameof(magnitude), $"量级须为正，实得 {magnitude}");
        if (pixelsPerCount <= 0 || double.IsNaN(pixelsPerCount) || double.IsInfinity(pixelsPerCount))
            throw new ArgumentOutOfRangeException(nameof(pixelsPerCount), $"ppc 须为正有限数，实得 {pixelsPerCount}");
        _buckets[magnitude] = pixelsPerCount;
    }

    public (bool Seeded, bool Significant, double PixelsPerCount) ApplyOnlineObservation(
        double commandMagnitude, double observedPpc, double weight, double significanceThreshold)
    {
        if (observedPpc <= 0 || double.IsNaN(observedPpc) || double.IsInfinity(observedPpc))
            throw new ArgumentOutOfRangeException(nameof(observedPpc), $"观测 ppc 须为正有限数，实得 {observedPpc}");
        if (weight is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(weight), $"EMA 权重须在 (0,1]，实得 {weight}");

        if (_buckets.Count == 0)
        {
            var seed = Math.Max(1, (int)Math.Round(Math.Abs(commandMagnitude)));
            _buckets[seed] = observedPpc;
            return (Seeded: true, Significant: true, PixelsPerCount: observedPpc);
        }

        var magnitude = Math.Abs(commandMagnitude);
        var bestKey = _buckets.Keys[0];
        var bestDistance = Math.Abs(magnitude - bestKey);
        foreach (var key in _buckets.Keys)
        {
            var distance = Math.Abs(magnitude - key);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestKey = key;
            }
        }

        var old = _buckets[bestKey];
        var blended = (1 - weight) * old + weight * observedPpc;
        _buckets[bestKey] = blended;
        var significant = Math.Abs(blended - old) / old > significanceThreshold;
        return (Seeded: false, Significant: significant, PixelsPerCount: blended);
    }

    public void Invalidate() => _buckets.Clear();
}
