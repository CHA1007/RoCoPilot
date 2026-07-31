namespace RocoPilot.Detection;

internal static class YoloPostprocessing
{
    internal static List<DetectedBox> ExtractDetections(
        ReadOnlySpan<float> output,
        IReadOnlyList<string> classNames,
        double confidenceThreshold,
        double iouThreshold,
        int maxBoxes,
        LetterboxGeometry geometry,
        int sourceWidth,
        int sourceHeight,
        IReadOnlyCollection<string>? whitelist = null)
    {
        var classCount = classNames.Count;
        if (classCount == 0)
            throw new ArgumentException("类名表不得为空", nameof(classNames));
        var channels = 4 + classCount;
        if (output.Length == 0 || output.Length % channels != 0)
            throw new ArgumentException($"输出张量长度须为 (4+nc)×na 且非零，实得 {output.Length}（nc={classCount}）", nameof(output));
        if (maxBoxes <= 0)
            throw new ArgumentException($"截顶须为正，实得 {maxBoxes}", nameof(maxBoxes));

        var anchors = output.Length / channels;
        var hasWhitelist = whitelist is { Count: > 0 };

        var candidates = new List<Candidate>();
        for (var a = 0; a < anchors; a++)
        {
            var bestClass = 0;
            var bestScore = output[4 * anchors + a];
            for (var c = 1; c < classCount; c++)
            {
                var score = output[(4 + c) * anchors + a];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            if ((double)bestScore <= confidenceThreshold) continue;
            if (hasWhitelist && !whitelist!.Contains(classNames[bestClass])) continue;

            var cx = output[a];
            var cy = output[anchors + a];
            var w = output[2 * anchors + a];
            var h = output[3 * anchors + a];
            candidates.Add(new Candidate(bestClass, bestScore, cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f));
        }

        var kept = new List<Candidate>(candidates.Count);
        foreach (var classIndex in candidates.Select(c => c.ClassIndex).Distinct())
        {
            foreach (var box in candidates.Where(c => c.ClassIndex == classIndex).OrderByDescending(c => c.Confidence))
            {
                var suppressed = false;
                foreach (var keeper in kept)
                {
                    if (keeper.ClassIndex == classIndex && Iou(box, keeper) >= iouThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (!suppressed) kept.Add(box);
            }
        }

        return kept
            .OrderByDescending(c => c.Confidence)
            .Take(maxBoxes)
            .Select(c => new DetectedBox(
                c.ClassIndex,
                classNames[c.ClassIndex],
                c.Confidence,
                Unmap(c.X1, geometry.PadLeft, geometry.Ratio, sourceWidth),
                Unmap(c.Y1, geometry.PadTop, geometry.Ratio, sourceHeight),
                Unmap(c.X2, geometry.PadLeft, geometry.Ratio, sourceWidth),
                Unmap(c.Y2, geometry.PadTop, geometry.Ratio, sourceHeight)))
            .ToList();
    }

    private static float Unmap(float value, int pad, double ratio, int max) =>
        (float)Math.Clamp((value - pad) / ratio, 0.0, max);

    internal static double Iou(Candidate a, Candidate b)
    {
        var iw = Math.Max(0.0, (double)Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var ih = Math.Max(0.0, (double)Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var inter = iw * ih;
        var areaA = (double)(a.X2 - a.X1) * (a.Y2 - a.Y1);
        var areaB = (double)(b.X2 - b.X1) * (b.Y2 - b.Y1);
        return inter / Math.Max(areaA + areaB - inter, 1e-9);
    }

    internal readonly record struct Candidate(int ClassIndex, float Confidence, float X1, float Y1, float X2, float Y2);
}
