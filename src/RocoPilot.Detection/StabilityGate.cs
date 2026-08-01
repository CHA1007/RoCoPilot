namespace RocoPilot.Detection;

public sealed record StableTarget(int TrackId, DetectedBox Latest, (float X, float Y) MedianCenter, int ConsecutiveFrames);

public sealed class StabilityGate
{
    private readonly int _stableFrames;
    private readonly double _spreadTolerancePx;
    private readonly double _associationRadiusSquared;
    private readonly List<Track> _tracks = [];
    private int _nextTrackId;

    public StabilityGate(int stableFrames = 4, double spreadTolerancePx = 300, double associationRadiusPx = 300)
    {
        _stableFrames = Math.Max(1, stableFrames);
        if (spreadTolerancePx <= 0 || double.IsNaN(spreadTolerancePx) || double.IsInfinity(spreadTolerancePx))
            throw new ArgumentException($"展幅容差须为正有限数，实得 {spreadTolerancePx}", nameof(spreadTolerancePx));
        if (associationRadiusPx <= 0 || double.IsNaN(associationRadiusPx) || double.IsInfinity(associationRadiusPx))
            throw new ArgumentException($"关联半径须为正有限数，实得 {associationRadiusPx}", nameof(associationRadiusPx));

        _spreadTolerancePx = spreadTolerancePx;
        _associationRadiusSquared = associationRadiusPx * associationRadiusPx;
    }

    public IReadOnlyList<StableTarget> Update(IReadOnlyList<DetectedBox> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);

        var matchedDetection = new int[_tracks.Count];
        Array.Fill(matchedDetection, -1);
        var taken = new bool[detections.Count];
        if (_tracks.Count > 0 && detections.Count > 0)
        {
            var pairs = new List<(int Track, int DetectionIndex, double DistanceSquared)>(_tracks.Count * detections.Count);
            for (var t = 0; t < _tracks.Count; t++)
            {
                var track = _tracks[t];
                for (var d = 0; d < detections.Count; d++)
                {
                    var dx = detections[d].CenterX - track.LatestCenterX;
                    var dy = detections[d].CenterY - track.LatestCenterY;
                    var distanceSquared = (double)dx * dx + (double)dy * dy;
                    if (distanceSquared <= _associationRadiusSquared)
                        pairs.Add((t, d, distanceSquared));
                }
            }

            foreach (var (track, detection, _) in pairs
                         .OrderBy(p => p.DistanceSquared).ThenBy(p => p.Track).ThenBy(p => p.DetectionIndex))
            {
                if (matchedDetection[track] != -1 || taken[detection]) continue;
                matchedDetection[track] = detection;
                taken[detection] = true;
            }
        }

        var next = new List<Track>(_tracks.Count + detections.Count);
        for (var t = 0; t < _tracks.Count; t++)
        {
            var d = matchedDetection[t];
            if (d < 0) continue;
            _tracks[t].Append(detections[d]);
            next.Add(_tracks[t]);
        }

        for (var d = 0; d < detections.Count; d++)
        {
            if (!taken[d])
                next.Add(new Track(_nextTrackId++, detections[d], _stableFrames));
        }

        _tracks.Clear();
        _tracks.AddRange(next);

        var result = new List<StableTarget>(_tracks.Count);
        foreach (var track in _tracks)
        {
            if (track.Consecutive < _stableFrames || track.CenterSpread() > _spreadTolerancePx)
                continue;
            result.Add(new StableTarget(track.Id, track.Latest, track.MedianCenter(), track.Consecutive));
        }

        return result;
    }

    public void Reset() => _tracks.Clear();

    private sealed class Track(int id, DetectedBox first, int window)
    {
        private readonly List<(float X, float Y)> _centers = [(first.CenterX, first.CenterY)];

        public int Id { get; } = id;

        public DetectedBox Latest { get; private set; } = first;

        public int Consecutive { get; private set; } = 1;

        public float LatestCenterX => Latest.CenterX;

        public float LatestCenterY => Latest.CenterY;

        public void Append(DetectedBox detection)
        {
            Latest = detection;
            Consecutive++;
            _centers.Add((detection.CenterX, detection.CenterY));
            if (_centers.Count > window)
                _centers.RemoveAt(0);
        }

        public double CenterSpread()
        {
            var minX = float.MaxValue; var maxX = float.MinValue;
            var minY = float.MaxValue; var maxY = float.MinValue;
            foreach (var (x, y) in _centers)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            return Math.Max((double)maxX - minX, (double)maxY - minY);
        }

        public (float X, float Y) MedianCenter()
        {
            var xs = _centers.Select(c => c.X).OrderBy(v => v).ToArray();
            var ys = _centers.Select(c => c.Y).OrderBy(v => v).ToArray();
            return (Median(xs), Median(ys));
        }

        private static float Median(float[] sorted)
        {
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2f;
        }
    }
}
