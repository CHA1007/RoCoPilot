using RocoPilot.Detection;

namespace RocoPilot.Loop;

public static class TargetSelection
{
    public static StableTarget? Pick(
        IReadOnlyList<StableTarget> stableTargets, int? lockedTrackId, float anchorX, float anchorY)
    {
        ArgumentNullException.ThrowIfNull(stableTargets);
        if (stableTargets.Count == 0)
        {
            return null;
        }

        if (lockedTrackId is { } track)
        {
            foreach (var target in stableTargets)
            {
                if (target.TrackId == track)
                {
                    return target;
                }
            }

            return null;
        }

        StableTarget? best = null;
        var bestDistance = double.MaxValue;
        foreach (var target in stableTargets)
        {
            var dx = (double)target.MedianCenter.X - anchorX;
            var dy = (double)target.MedianCenter.Y - anchorY;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance == bestDistance && best is not null && target.TrackId < best.TrackId))
            {
                bestDistance = distance;
                best = target;
            }
        }

        return best;
    }
}
